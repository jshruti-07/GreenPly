-- =============================================================================
-- MODULE 10: NORMALIZE WORKFLOW STATES & ENSURE WORKFLOW PIPELINE SEEDS
-- =============================================================================
-- 1. Normalizes all product.workflow_state values to lowercase to prevent
--    case-variant collision in dictionary aggregations.
-- 2. Ensures the active 'product_publishing' workflow definition, steps, and
--    transitions are present.
-- 3. Ensures approval_task instances exist for all non-terminal products.
-- =============================================================================

BEGIN;
SET search_path TO catalog, public;

-- 1. Normalize existing product workflow states to lowercase
UPDATE product
SET workflow_state = LOWER(workflow_state)
WHERE workflow_state <> LOWER(workflow_state);

-- 2. Ensure active workflow definition exists
INSERT INTO workflow_definition (code, name, entity_type, version, is_active)
VALUES ('product_publishing', 'Product Publishing', 'PRODUCT', 1, true)
ON CONFLICT (code, version) DO UPDATE SET is_active = true;

-- 3. Ensure workflow steps exist
INSERT INTO workflow_step (definition_id, code, name, sort_order, owner_role, is_initial, is_terminal)
SELECT d.id, v.code, v.name, v.sort_order, v.owner_role, v.is_initial, v.is_terminal
FROM workflow_definition d
CROSS JOIN (VALUES
  ('draft',     'Draft',     1, 'Data Steward',       true,  false),
  ('review',    'Review',    2, 'Product Manager',    false, false),
  ('approval',  'Approval',  3, 'Compliance Manager', false, false),
  ('published', 'Published', 4, NULL,                 false, true)
) AS v(code, name, sort_order, owner_role, is_initial, is_terminal)
WHERE d.code = 'product_publishing' AND d.version = 1
ON CONFLICT (definition_id, code) DO NOTHING;

-- 4. Ensure workflow transitions exist
INSERT INTO workflow_transition (definition_id, from_step_id, to_step_id, action, label, sort_order)
SELECT d.id, fs.id, ts.id, v.action, v.label, v.sort_order
FROM workflow_definition d
CROSS JOIN (VALUES
  ('draft',    'review',    'SUBMIT',          'Submit for review',   1),
  ('review',   'approval',  'APPROVE',         'Pass review',         1),
  ('review',   'draft',     'REQUEST_CHANGES', 'Request changes',     2),
  ('approval', 'published', 'APPROVE',         'Approve and publish', 1),
  ('approval', 'draft',     'REJECT',          'Reject',              2),
  ('approval', 'draft',     'REQUEST_CHANGES', 'Request changes',     3)
) AS v(from_code, to_code, action, label, sort_order)
JOIN workflow_step fs ON fs.definition_id = d.id AND fs.code = v.from_code
JOIN workflow_step ts ON ts.definition_id = d.id AND ts.code = v.to_code
WHERE d.code = 'product_publishing' AND d.version = 1
ON CONFLICT (definition_id, from_step_id, action) DO NOTHING;

-- 5. Ensure approval tasks exist for open/non-terminal products
INSERT INTO approval_task (product_id, definition_id, current_step_id, assignee_display_name, status, priority)
SELECT p.id, d.id, ws.id, COALESCE(p.owner_display_name, 'Data Steward'), 'open', 'normal'
FROM product p
JOIN workflow_definition d ON d.entity_type = 'PRODUCT' AND d.is_active
JOIN workflow_step ws ON ws.definition_id = d.id AND ws.code = LOWER(p.workflow_state)
WHERE NOT ws.is_terminal
  AND NOT EXISTS (
    SELECT 1 FROM approval_task t WHERE t.product_id = p.id AND t.status = 'open'
  );

COMMIT;
