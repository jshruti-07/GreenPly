# API Gateway

Per the technology stack matrix: NGINX Ingress Controller on Kubernetes is the
gateway — no custom gateway code. `ingress.yaml` is the routing contract:

| Route prefix          | Service          |
|-----------------------|------------------|
| /api/v1/products      | catalog-service  |
| /api/v1/taxonomy      | catalog-service  |
| /api/v1/assets        | asset-service (planned) |
| /api/v1/search        | search-service (planned) |
| /                     | frontend         |

Local development does not run a gateway; the Vite dev server proxies /api to
the catalog service (see frontend/vite.config.ts).

OAuth2/OIDC enforcement at the ingress (oauth2-proxy or provider-specific) is
blocked on the identity-provider decision (TBD in architecture v1.3).
