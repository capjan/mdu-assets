-- The asset list of a project is read newest first, page by page.
CREATE INDEX assets_project_created ON assets (project_id, created_at DESC, id DESC);
DROP INDEX assets_project;
