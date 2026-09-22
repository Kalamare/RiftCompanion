CREATE TABLE IF NOT EXISTS schema_version (version integer PRIMARY KEY);
INSERT INTO schema_version VALUES(1) ON CONFLICT DO NOTHING;

CREATE TABLE IF NOT EXISTS players (
 platform text NOT NULL, puuid text NOT NULL, riot_id text NOT NULL,
 level integer, icon integer, ranks jsonb NOT NULL DEFAULT '[]', ranks_at timestamptz,
 last_seen timestamptz NOT NULL, version bigint NOT NULL DEFAULT 0,
 PRIMARY KEY(platform,puuid)
);
-- Only explicit lookups enter this table. Participants never create jobs.
CREATE TABLE IF NOT EXISTS profile_jobs (
 id uuid PRIMARY KEY, platform text NOT NULL, riot_id text NOT NULL, normalized_id text NOT NULL,
 puuid text, active_until timestamptz NOT NULL, due_at timestamptz NOT NULL DEFAULT now(),
 lease_until timestamptz, state text NOT NULL DEFAULT 'queued', error text,
 cursor integer NOT NULL DEFAULT 0, coverage_from timestamptz NOT NULL, window_from timestamptz NOT NULL, window_until timestamptz NOT NULL,
 scanned boolean NOT NULL DEFAULT false, missing integer NOT NULL DEFAULT 0,
 completed_at timestamptz, attempts integer NOT NULL DEFAULT 0,
 UNIQUE(platform,normalized_id)
);
CREATE INDEX IF NOT EXISTS jobs_due ON profile_jobs(due_at);
CREATE INDEX IF NOT EXISTS jobs_player ON profile_jobs(platform,puuid);
-- v2: independent recent/history cursors. Existing season jobs and matches are preserved.
ALTER TABLE profile_jobs ADD COLUMN IF NOT EXISTS kind text NOT NULL DEFAULT 'history';
ALTER TABLE profile_jobs ADD COLUMN IF NOT EXISTS refresh_after timestamptz;
ALTER TABLE profile_jobs DROP CONSTRAINT IF EXISTS profile_jobs_platform_normalized_id_key;
CREATE UNIQUE INDEX IF NOT EXISTS jobs_name_kind ON profile_jobs(platform,normalized_id,kind);
CREATE UNIQUE INDEX IF NOT EXISTS jobs_recent_player ON profile_jobs(platform,puuid) WHERE kind='recent';
CREATE TABLE IF NOT EXISTS user_work_requests (actor text NOT NULL, kind text NOT NULL, at timestamptz NOT NULL DEFAULT now());
CREATE INDEX IF NOT EXISTS user_work_time ON user_work_requests(actor,kind,at);
CREATE TABLE IF NOT EXISTS collector_turn (id integer PRIMARY KEY, value bigint NOT NULL);
INSERT INTO collector_turn VALUES(1,0) ON CONFLICT DO NOTHING;
INSERT INTO schema_version VALUES(2) ON CONFLICT DO NOTHING;
INSERT INTO profile_jobs(id,platform,riot_id,normalized_id,puuid,kind,active_until,coverage_from,window_from,window_until)
 SELECT gen_random_uuid(),j.platform,j.riot_id,j.puuid,j.puuid,'recent',j.active_until,
   LEAST(now(),COALESCE(j.completed_at,j.window_until))-interval '48 hours',
   LEAST(now(),COALESCE(j.completed_at,j.window_until))-interval '48 hours',date_trunc('second',now())
 FROM (SELECT DISTINCT ON (platform,puuid) * FROM profile_jobs WHERE kind='history' AND puuid IS NOT NULL ORDER BY platform,puuid,due_at) j
 ON CONFLICT(platform,puuid) WHERE kind='recent' DO NOTHING;
CREATE TABLE IF NOT EXISTS unavailable_matches (
 job_id uuid NOT NULL REFERENCES profile_jobs(id), match_id text NOT NULL, PRIMARY KEY(job_id,match_id)
);
CREATE TABLE IF NOT EXISTS matches (
 region text NOT NULL, match_id text NOT NULL, played_at timestamptz NOT NULL, details jsonb NOT NULL,
 PRIMARY KEY(region,match_id)
);
CREATE TABLE IF NOT EXISTS contributions (
 region text NOT NULL, match_id text NOT NULL, platform text NOT NULL, puuid text NOT NULL,
 PRIMARY KEY(region,match_id,puuid),
 FOREIGN KEY(region,match_id) REFERENCES matches(region,match_id)
);
CREATE INDEX IF NOT EXISTS contributions_player ON contributions(platform,puuid);
CREATE INDEX IF NOT EXISTS matches_date ON matches(played_at DESC,match_id DESC);
-- Additive buckets retain weighted numerators, not averages of averages.
CREATE TABLE IF NOT EXISTS daily_stats (
 platform text NOT NULL, puuid text NOT NULL, day date NOT NULL, queue integer NOT NULL,
 champion_id integer NOT NULL, champion text NOT NULL, role text NOT NULL,
 games bigint NOT NULL, wins bigint NOT NULL, kills bigint NOT NULL, deaths bigint NOT NULL, assists bigint NOT NULL,
 cs bigint NOT NULL, damage bigint NOT NULL, gold bigint NOT NULL, vision bigint NOT NULL,
 wards bigint NOT NULL, wards_killed bigint NOT NULL, control_wards bigint NOT NULL,
 seconds double precision NOT NULL, kp_sum double precision NOT NULL, kp_count bigint NOT NULL,
 PRIMARY KEY(platform,puuid,day,queue,champion_id,role)
);
CREATE TABLE IF NOT EXISTS profile_changes (
 sequence bigserial PRIMARY KEY, platform text NOT NULL, puuid text NOT NULL, version bigint NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS riot_requests (host text NOT NULL, at timestamptz NOT NULL DEFAULT now());
CREATE INDEX IF NOT EXISTS riot_requests_host ON riot_requests(host,at);
CREATE TABLE IF NOT EXISTS riot_cooldowns (host text PRIMARY KEY, until_at timestamptz NOT NULL);
CREATE TABLE IF NOT EXISTS riot_windows (host text NOT NULL, seconds integer NOT NULL, quota integer NOT NULL,
 PRIMARY KEY(host,seconds));
