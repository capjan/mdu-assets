-- Nested folders per project (ORG-01). Folders only organize; they never appear in a link.
CREATE TABLE folders (
    id         INTEGER PRIMARY KEY,
    project_id INTEGER NOT NULL REFERENCES projects (id) ON DELETE CASCADE,
    parent_id  INTEGER REFERENCES folders (id),
    name       TEXT    NOT NULL,
    created_at TEXT    NOT NULL
);

-- Sibling folders have different names; top-level folders count as siblings too.
CREATE UNIQUE INDEX folders_sibling_names ON folders (project_id, coalesce(parent_id, 0), name COLLATE NOCASE);

ALTER TABLE assets ADD COLUMN folder_id INTEGER REFERENCES folders (id);
ALTER TABLE assets ADD COLUMN description TEXT NOT NULL DEFAULT '';

CREATE INDEX assets_folder ON assets (folder_id);

-- Free tags per asset (ORG-03). `key` is the lower-case form from .NET, so tags compare without case for all
-- letters, not only A to Z as SQLite's NOCASE does; `name` keeps the spelling shown.
CREATE TABLE tags (
    asset_id TEXT NOT NULL REFERENCES assets (id),
    key      TEXT NOT NULL,
    name     TEXT NOT NULL,
    PRIMARY KEY (asset_id, key)
);

CREATE INDEX tags_key ON tags (key);
