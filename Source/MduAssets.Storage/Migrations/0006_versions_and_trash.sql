-- Deleted assets wait in the trash (DEL-01, DEL-02); their links answer 410 Gone (DLV-07).
ALTER TABLE assets ADD COLUMN deleted_at TEXT;
ALTER TABLE assets ADD COLUMN deleted_by INTEGER REFERENCES users (id);

-- After the final deletion, files and versions are gone but the row stays, so the id is never reused and links keep answering 410.
ALTER TABLE assets ADD COLUMN purged_at TEXT;

CREATE INDEX assets_deleted ON assets (project_id, deleted_at);

-- Ids of assets whose rows are gone with their project; new assets never get them again.
CREATE TABLE retired_asset_ids (
    id TEXT PRIMARY KEY
);

-- Optional limit of kept versions per asset (VER-04, ADR-0016); NULL keeps all.
ALTER TABLE projects ADD COLUMN max_versions INTEGER CHECK (max_versions IS NULL OR max_versions >= 1);
