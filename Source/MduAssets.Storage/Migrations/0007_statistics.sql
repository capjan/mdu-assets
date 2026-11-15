-- Requests per asset (STA-01): only a count and the time of the last request, nothing about the reader (STA-06).
CREATE TABLE asset_stats (
    asset_id    TEXT    PRIMARY KEY REFERENCES assets (id),
    hit_count   INTEGER NOT NULL,
    last_hit_at TEXT    NOT NULL
);

-- Referring pages (STA-04): origin and path without query or fragment, at most 100 per asset.
CREATE TABLE asset_referrers (
    asset_id    TEXT    NOT NULL REFERENCES assets (id),
    origin      TEXT    NOT NULL,
    path        TEXT    NOT NULL,
    hit_count   INTEGER NOT NULL,
    last_hit_at TEXT    NOT NULL,
    PRIMARY KEY (asset_id, origin, path)
);
