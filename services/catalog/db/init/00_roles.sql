-- Local development only. In K8s these credentials come from Secrets.
-- Each service gets its own role; catalog_svc can only touch the catalog schema.
CREATE ROLE catalog_svc LOGIN PASSWORD 'catalog_local_dev';
