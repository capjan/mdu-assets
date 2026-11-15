-- Per-project limits (OPS-01, OPS-02): a lower upload limit set by owners and a storage quota set by admins; NULL means none.
ALTER TABLE projects ADD COLUMN max_file_bytes INTEGER CHECK (max_file_bytes IS NULL OR max_file_bytes > 0);
ALTER TABLE projects ADD COLUMN quota_bytes INTEGER CHECK (quota_bytes IS NULL OR quota_bytes > 0);

-- Audit log (OPS-03). Names are copied, so entries stay readable after users or projects are gone.
CREATE TABLE audit_log (
    id           INTEGER PRIMARY KEY,
    at           TEXT    NOT NULL,
    user_id      INTEGER,
    user_name    TEXT,
    project_id   INTEGER,
    project_slug TEXT,
    action       TEXT    NOT NULL,
    target       TEXT,
    details      TEXT,
    ip           TEXT
);

CREATE INDEX audit_log_at ON audit_log (at);
CREATE INDEX audit_log_project ON audit_log (project_id, at);
