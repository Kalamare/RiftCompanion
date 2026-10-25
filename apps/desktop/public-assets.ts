import { readFile, writeFile, mkdir, rename } from "node:fs/promises";
import { join } from "node:path";
import { z } from "zod";
import { limitedJson } from "../server/riot";
import type { PublicAsset } from "./ui/assets";
export const assetInput = z
  .object({
    kind: z.enum(["item", "profileicon"]),
    id: z.number().int().min(0).max(1000000),
  })
  .strict();
const strip = (text: string) =>
  text
    .replace(/<br\s*\/?\s*>/gi, "\n")
    .replace(/<[^>]*>/g, "")
    .replace(/@[A-Za-z0-9_.]+@/g, "valeur indisponible")
    .replace(/&nbsp;/g, " ")
    .replace(/&amp;/g, "&");
// Public CDN only: no Riot/server credentials, no arbitrary host/path supplied over IPC.
export class PublicAssets {
  private pending = new Map<string, Promise<PublicAsset>>();
  private catalog: Promise<any> | null = null;
  private version = "16.18.1";
  private active = 0;
  private waiters: (() => void)[] = [];
  constructor(
    private folder: string,
    private fetcher: typeof fetch = fetch,
  ) {}
  private async limited<T>(fn: () => Promise<T>) {
    if (this.active >= 4)
      await new Promise<void>((resolve) => this.waiters.push(resolve));
    this.active++;
    try {
      return await fn();
    } finally {
      this.active--;
      this.waiters.shift()?.();
    }
  }
  private async response(path: string) {
    return this.fetcher("https://ddragon.leagueoflegends.com/" + path, {
      redirect: "error",
      credentials: "omit",
      signal: AbortSignal.timeout(8000),
    });
  }
  private async items() {
    if (!this.catalog)
      this.catalog = (async () => {
        await mkdir(this.folder, { recursive: true });
        let saved: any = null;
        try {
          saved = JSON.parse(
            await readFile(join(this.folder, "catalog.json"), "utf8"),
          );
          if (/^\d+\.\d+\.\d+$/.test(saved.version)) {
            this.version = saved.version;
            if (Date.now() - saved.at < 86400000) return saved.data;
          }
        } catch {}
        try {
          const versions = await this.response("api/versions.json");
          if (!versions.ok) throw new Error("cdn_unavailable");
          const v = await limitedJson(versions, 100000);
          if (!Array.isArray(v) || !/^\d+\.\d+\.\d+$/.test(v[0]))
            throw new Error("invalid_version");
          this.version = v[0];
          const response = await this.response(
            `cdn/${this.version}/data/fr_FR/item.json`,
          );
          if (!response.ok) throw new Error("cdn_unavailable");
          const data = await limitedJson(response);
          if (!data.data || typeof data.data !== "object")
            throw new Error("invalid_catalog");
          await writeFile(
            join(this.folder, "catalog.json.tmp"),
            JSON.stringify({
              version: this.version,
              at: Date.now(),
              data: data.data,
            }),
          );
          await rename(
            join(this.folder, "catalog.json.tmp"),
            join(this.folder, "catalog.json"),
          );
          return data.data;
        } catch {
          return saved?.data ?? {};
        }
      })();
    return this.catalog;
  }
  get(input: unknown) {
    const { kind, id } = assetInput.parse(input);
    const key = kind + "-" + id;
    if (!this.pending.has(key))
      this.pending.set(
        key,
        this.limited(async () => {
          const catalog = await this.items();
          const entry = kind === "item" ? catalog[String(id)] : null;
          const result: PublicAsset = {
            image: null,
            name:
              entry?.name ??
              (kind === "item"
                ? `Objet ${id} (catalogue indisponible)`
                : "Icône de profil"),
            description: strip(entry?.description ?? ""),
            price: Number.isFinite(entry?.gold?.total)
              ? entry.gold.total
              : null,
            components: Array.isArray(entry?.from)
              ? entry.from
                  .map(Number)
                  .filter((n: number) => Number.isInteger(n) && n > 0)
              : [],
          };
          const file = join(this.folder, `${this.version}-${kind}-${id}.png`);
          let bytes: Buffer;
          try {
            bytes = await readFile(file);
          } catch {
            try {
              const response = await this.response(
                `cdn/${this.version}/img/${kind}/${id}.png`,
              );
              if (!response.ok) {
                await response.body?.cancel();
                return result;
              }
              const reader = response.body!.getReader();
              const chunks: Uint8Array[] = [];
              let total = 0;
              try {
                for (;;) {
                  const chunk = await reader.read();
                  if (chunk.done) break;
                  total += chunk.value.length;
                  if (total > 512000) throw new Error("image_too_large");
                  chunks.push(chunk.value);
                }
              } finally {
                await reader.cancel();
              }
              bytes = Buffer.concat(chunks);
              if (bytes.subarray(0, 8).toString("hex") !== "89504e470d0a1a0a")
                return result;
              await writeFile(file, bytes);
            } catch {
              return result;
            }
          }
          if (
            bytes.length <= 512000 &&
            bytes.subarray(0, 8).toString("hex") === "89504e470d0a1a0a"
          )
            result.image = "data:image/png;base64," + bytes.toString("base64");
          return result;
        }),
      );
    return this.pending.get(key)!;
  }
}
