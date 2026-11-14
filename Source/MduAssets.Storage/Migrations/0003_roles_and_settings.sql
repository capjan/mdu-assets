-- Start passwords set by an admin must be changed by the user (AUTH-05).
ALTER TABLE users ADD COLUMN must_change_password INTEGER NOT NULL DEFAULT 0;

ALTER TABLE projects ADD COLUMN description TEXT NOT NULL DEFAULT '';

-- Who works in which project, and with which role (section 4.2 of the requirements).
CREATE TABLE memberships (
    project_id INTEGER NOT NULL REFERENCES projects (id) ON DELETE CASCADE,
    user_id    INTEGER NOT NULL REFERENCES users (id),
    role       TEXT    NOT NULL CHECK (role IN ('viewer', 'editor', 'owner')),
    created_at TEXT    NOT NULL,
    PRIMARY KEY (project_id, user_id)
);

CREATE INDEX memberships_user ON memberships (user_id);

-- Instance settings that admins change at run time, as text values.
CREATE TABLE settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- Projects created before roles existed get the existing admins as owners, so every project has one.
INSERT INTO memberships (project_id, user_id, role, created_at)
SELECT p.id, u.id, 'owner', strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
FROM projects p
JOIN users u ON u.is_admin = 1;
