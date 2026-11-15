-- Personal API tokens for tools (AUTH-06): shown once, stored as SHA-256 hash, optionally limited to a project and to reading.
CREATE TABLE api_tokens (
    id           INTEGER PRIMARY KEY,
    user_id      INTEGER NOT NULL REFERENCES users (id),
    name         TEXT    NOT NULL,
    prefix       TEXT    NOT NULL,
    hash         TEXT    NOT NULL UNIQUE,
    project_id   INTEGER REFERENCES projects (id) ON DELETE CASCADE,
    scope        TEXT    NOT NULL CHECK (scope IN ('read', 'write')),
    expires_at   TEXT,
    last_used_at TEXT,
    created_at   TEXT    NOT NULL
);

CREATE INDEX api_tokens_user ON api_tokens (user_id);
