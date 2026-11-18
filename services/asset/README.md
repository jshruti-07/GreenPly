# asset-service (planned — not yet implemented)

Bounded context: digital assets (DAM) — `asset`, `asset_rendition`, `asset_tr`,
`asset_link` tables from pim_database_v2.0.sql, carved into an `asset` schema
when implementation starts. Follows the same Clean Architecture layout as
services/catalog. Links to products are logical references (public_id / code),
consistency via events — no cross-schema FKs.
