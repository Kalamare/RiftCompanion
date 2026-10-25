# syntax=docker/dockerfile:1
FROM node:24-bookworm-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN --mount=type=secret,id=proxy_ca,required=false \
    if [ -f /run/secrets/proxy_ca ]; then export NODE_EXTRA_CA_CERTS=/run/secrets/proxy_ca; fi; \
    ELECTRON_SKIP_BINARY_DOWNLOAD=1 npm ci --ignore-scripts --no-audit --no-fund
COPY apps ./apps
COPY packages ./packages
COPY server/Rift.Backend/schema.sql ./server/Rift.Backend/schema.sql
RUN node_modules/.bin/esbuild apps/server/api.ts apps/server/worker.ts --bundle --platform=node --format=cjs --target=node24 --external:pg-native --outdir=dist/server
FROM node:24-bookworm-slim AS runtime
ENV NODE_ENV=production NODE_USE_ENV_PROXY=1 HOST=0.0.0.0 PORT=8080 RIFT_SCHEMA_PATH=/app/schema.sql
WORKDIR /app
COPY --from=build --chown=node:node /app/dist/server/ ./
COPY --from=build --chown=node:node /app/server/Rift.Backend/schema.sql ./schema.sql
USER node
EXPOSE 8080
CMD ["node","api.js"]
