-- Users who sign in to the UI and the API.
CREATE TABLE users (
    id            INTEGER PRIMARY KEY,
    name          TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    password_hash TEXT    NOT NULL,
    is_admin      INTEGER NOT NULL DEFAULT 0,
    is_locked     INTEGER NOT NULL DEFAULT 0,
    created_at    TEXT    NOT NULL
);

-- Containers for assets. Until projects are managed (M3), every instance has the project "default".
CREATE TABLE projects (
    id         INTEGER PRIMARY KEY,
    slug       TEXT NOT NULL UNIQUE,
    name       TEXT NOT NULL,
    created_at TEXT NOT NULL
);

INSERT INTO projects (slug, name, created_at) VALUES ('default', 'Default', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

-- An asset is the unit behind one public link. Its id is never changed or reused.
CREATE TABLE assets (
    id              TEXT    PRIMARY KEY,
    project_id      INTEGER NOT NULL REFERENCES projects (id),
    file_name       TEXT    NOT NULL,
    alt_text        TEXT    NOT NULL,
    current_version INTEGER NOT NULL,
    created_by      INTEGER NOT NULL REFERENCES users (id),
    created_at      TEXT    NOT NULL
);

CREATE INDEX assets_project ON assets (project_id);

-- Immutable contents of an asset. Versions with the same content share a blob.
CREATE TABLE asset_versions (
    asset_id     TEXT    NOT NULL REFERENCES assets (id),
    number       INTEGER NOT NULL,
    blob_sha256  TEXT    NOT NULL,
    content_type TEXT    NOT NULL,
    size         INTEGER NOT NULL,
    width        INTEGER,
    height       INTEGER,
    created_by   INTEGER NOT NULL REFERENCES users (id),
    created_at   TEXT    NOT NULL,
    PRIMARY KEY (asset_id, number)
);
