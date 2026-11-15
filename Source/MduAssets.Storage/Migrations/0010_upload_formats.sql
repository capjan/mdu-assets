-- The upload a version was converted from (UP-11): kept for exports and later conversions, never served publicly.
-- NULL when the version is the uploaded file itself.
ALTER TABLE asset_versions ADD COLUMN original_sha256 TEXT;
ALTER TABLE asset_versions ADD COLUMN original_content_type TEXT;
ALTER TABLE asset_versions ADD COLUMN original_size INTEGER;

CREATE INDEX asset_versions_original ON asset_versions (original_sha256) WHERE original_sha256 IS NOT NULL;
