-- Tags instead of folders (ADR-0020, ORG-03): an image gets its folder and every folder above it as tags. A tag the project
-- already uses keeps its spelling. Keys are lower-cased by .NET (unicode_lower), as for every other tag.
WITH RECURSIVE chain (asset_id, folder_id) AS (
    SELECT id, folder_id FROM assets WHERE folder_id IS NOT NULL
    UNION
    SELECT c.asset_id, f.parent_id FROM chain c JOIN folders f ON f.id = c.folder_id WHERE f.parent_id IS NOT NULL
),
named (asset_id, project_id, name) AS (
    SELECT c.asset_id, a.project_id, trim(substr(trim(f.name), 1, 50))
    FROM chain c JOIN folders f ON f.id = c.folder_id JOIN assets a ON a.id = c.asset_id
)
INSERT OR IGNORE INTO tags (asset_id, key, name)
SELECT n.asset_id, unicode_lower(n.name),
       coalesce((SELECT t.name FROM tags t JOIN assets o ON o.id = t.asset_id
                 WHERE o.project_id = n.project_id AND t.key = unicode_lower(n.name) LIMIT 1), n.name)
FROM named n WHERE n.name <> '';

-- Folders are gone. SQLite cannot drop a column that has a foreign key without rebuilding the assets table, so
-- assets.folder_id and the folders table stay, always empty; nothing reads or writes them any more.
UPDATE assets SET folder_id = NULL WHERE folder_id IS NOT NULL;
DELETE FROM folders;
DROP INDEX assets_folder;
DROP INDEX folders_sibling_names;
