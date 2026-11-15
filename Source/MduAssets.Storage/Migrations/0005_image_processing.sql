-- Animated GIF and WebP keep all frames and are never converted to another format (DLV-03, DLV-04).
ALTER TABLE asset_versions ADD COLUMN animated INTEGER NOT NULL DEFAULT 0;

-- Owners can turn WebP and AVIF delivery off for a project (DLV-04).
ALTER TABLE projects ADD COLUMN convert_formats INTEGER NOT NULL DEFAULT 1;

-- Two assets of a project can form a light and dark pair (LNK-06); both sides point at each other.
ALTER TABLE assets ADD COLUMN pair_asset_id TEXT REFERENCES assets (id);
ALTER TABLE assets ADD COLUMN pair_role TEXT CHECK (pair_role IN ('light', 'dark'));

-- Duplicate uploads are found by their content (UP-09).
CREATE INDEX asset_versions_blob ON asset_versions (blob_sha256);
