# search-service (planned — not yet implemented)

Bounded context: search & discovery. Consumes catalog events (RabbitMQ) to
project products into Elasticsearch/OpenSearch. Owns its index schemas; no
relational tables. Read-only query API for the frontend via the gateway.
