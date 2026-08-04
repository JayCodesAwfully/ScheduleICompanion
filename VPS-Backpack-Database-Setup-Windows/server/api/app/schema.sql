CREATE TABLE IF NOT EXISTS players (
    steam_id BIGINT PRIMARY KEY,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS api_tokens (
    token_hash CHAR(64) PRIMARY KEY,
    steam_id BIGINT NOT NULL REFERENCES players(steam_id) ON DELETE CASCADE,
    label VARCHAR(80) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_used_at TIMESTAMPTZ,
    revoked_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS backpack_revisions (
    steam_id BIGINT NOT NULL REFERENCES players(steam_id) ON DELETE CASCADE,
    career_id VARCHAR(160) NOT NULL,
    revision BIGINT NOT NULL CHECK (revision > 0),
    content_hash CHAR(64) NOT NULL,
    transaction_tail_hash CHAR(64),
    snapshot BYTEA NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    restored_from BIGINT,
    PRIMARY KEY (steam_id, career_id, revision)
);

CREATE TABLE IF NOT EXISTS backpack_heads (
    steam_id BIGINT NOT NULL REFERENCES players(steam_id) ON DELETE CASCADE,
    career_id VARCHAR(160) NOT NULL,
    revision BIGINT NOT NULL,
    content_hash CHAR(64) NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (steam_id, career_id),
    FOREIGN KEY (steam_id, career_id, revision)
        REFERENCES backpack_revisions(steam_id, career_id, revision)
);

CREATE INDEX IF NOT EXISTS backpack_revision_history
    ON backpack_revisions (steam_id, career_id, revision DESC);
