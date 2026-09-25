-- Current workflow projections and append-only transition history.
CREATE TABLE IF NOT EXISTS WorkflowStates (
    AggregateType TEXT NOT NULL,
    AggregateId TEXT NOT NULL,
    CurrentState TEXT NOT NULL,
    PreviousActiveState TEXT NULL,
    Version INTEGER NOT NULL DEFAULT 0,
    CONSTRAINT PK_WorkflowStates PRIMARY KEY (AggregateType, AggregateId)
);

CREATE TABLE IF NOT EXISTS WorkflowTransitionEvents (
    Id TEXT NOT NULL CONSTRAINT PK_WorkflowTransitionEvents PRIMARY KEY,
    AggregateType TEXT NOT NULL,
    AggregateId TEXT NOT NULL,
    FromState TEXT NOT NULL,
    ToState TEXT NOT NULL,
    Actor TEXT NOT NULL,
    Reason TEXT NOT NULL,
    OccurredAt TEXT NOT NULL,
    EvidenceJson TEXT NOT NULL,
    Version INTEGER NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_WorkflowTransitionEvents_Aggregate_Version
ON WorkflowTransitionEvents (AggregateType, AggregateId, Version);

INSERT OR IGNORE INTO WorkflowStates (AggregateType, AggregateId, CurrentState, Version)
SELECT 'PURCHASE_ORDER', Id, 'IDENTIFICADA_NO_LEGADO', 0 FROM PurchaseOrders;

INSERT OR IGNORE INTO WorkflowTransitionEvents
    (Id, AggregateType, AggregateId, FromState, ToState, Actor, Reason, OccurredAt, EvidenceJson, Version)
SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' || substr(lower(hex(randomblob(2))), 2) || '-a' || substr(lower(hex(randomblob(2))), 2) || '-' || lower(hex(randomblob(6))),
       'PURCHASE_ORDER', Id, 'IMPORTADO', 'IDENTIFICADA_NO_LEGADO', 'historical-import',
       'Estado inicial criado a partir do histórico Excel; não representa aprovação no TOTVS.',
       strftime('%Y-%m-%dT%H:%M:%f+00:00', 'now'), '{"source":"historical-excel","entity":"purchase-order"}', 0
FROM PurchaseOrders
WHERE NOT EXISTS (SELECT 1 FROM WorkflowTransitionEvents e WHERE e.AggregateType = 'PURCHASE_ORDER' AND e.AggregateId = PurchaseOrders.Id AND e.Version = 0);

INSERT OR IGNORE INTO WorkflowStates (AggregateType, AggregateId, CurrentState, Version)
SELECT 'IMPORT_PROCESS', Id,
       CASE upper(replace(trim(coalesce(LogisticsStatus, '')), ' ', '_'))
           WHEN 'WAITING_PRODUCTION' THEN 'EM_PRODUCAO'
           WHEN 'WAITING_SHIPMENT' THEN 'PRE_EMBARQUE'
           WHEN 'WAITING_ARRIVAL' THEN 'EM_TRANSITO'
           WHEN 'CUSTOMS_CLEARANCE' THEN 'DESEMBARACO'
           WHEN 'DELIVERED' THEN 'ENTREGUE'
           WHEN 'CANCELLED' THEN 'CANCELADO'
           WHEN 'NOVO' THEN 'NOVO'
           WHEN 'AGUARDANDO_PO' THEN 'AGUARDANDO_PO'
           WHEN 'EM_PRODUCAO' THEN 'EM_PRODUCAO'
           WHEN 'PRE_EMBARQUE' THEN 'PRE_EMBARQUE'
           WHEN 'BOOKING' THEN 'BOOKING'
           WHEN 'EMBARCADO' THEN 'EMBARCADO'
           WHEN 'EM_TRANSITO' THEN 'EM_TRANSITO'
           WHEN 'CHEGADA' THEN 'CHEGADA'
           WHEN 'DESEMBARACO' THEN 'DESEMBARACO'
           WHEN 'LIBERADO' THEN 'LIBERADO'
           WHEN 'ENTREGUE' THEN 'ENTREGUE'
           WHEN 'FINALIZADO' THEN 'FINALIZADO'
           ELSE 'INDETERMINADO'
       END, 0
FROM ImportProcesses;

INSERT OR IGNORE INTO WorkflowTransitionEvents
    (Id, AggregateType, AggregateId, FromState, ToState, Actor, Reason, OccurredAt, EvidenceJson, Version)
SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' || substr(lower(hex(randomblob(2))), 2) || '-a' || substr(lower(hex(randomblob(2))), 2) || '-' || lower(hex(randomblob(6))),
       'IMPORT_PROCESS', Id, 'IMPORTADO',
       CASE upper(replace(trim(coalesce(LogisticsStatus, '')), ' ', '_'))
           WHEN 'WAITING_PRODUCTION' THEN 'EM_PRODUCAO'
           WHEN 'WAITING_SHIPMENT' THEN 'PRE_EMBARQUE'
           WHEN 'WAITING_ARRIVAL' THEN 'EM_TRANSITO'
           WHEN 'CUSTOMS_CLEARANCE' THEN 'DESEMBARACO'
           WHEN 'DELIVERED' THEN 'ENTREGUE'
           WHEN 'CANCELLED' THEN 'CANCELADO'
           WHEN 'NOVO' THEN 'NOVO'
           WHEN 'AGUARDANDO_PO' THEN 'AGUARDANDO_PO'
           WHEN 'EM_PRODUCAO' THEN 'EM_PRODUCAO'
           WHEN 'PRE_EMBARQUE' THEN 'PRE_EMBARQUE'
           WHEN 'BOOKING' THEN 'BOOKING'
           WHEN 'EMBARCADO' THEN 'EMBARCADO'
           WHEN 'EM_TRANSITO' THEN 'EM_TRANSITO'
           WHEN 'CHEGADA' THEN 'CHEGADA'
           WHEN 'DESEMBARACO' THEN 'DESEMBARACO'
           WHEN 'LIBERADO' THEN 'LIBERADO'
           WHEN 'ENTREGUE' THEN 'ENTREGUE'
           WHEN 'FINALIZADO' THEN 'FINALIZADO'
           ELSE 'INDETERMINADO'
       END,
       'historical-import', 'Estado inicial mapeado do status logístico histórico; transições não foram presumidas.',
       strftime('%Y-%m-%dT%H:%M:%f+00:00', 'now'),
       json_object('source', 'historical-excel', 'legacyStatus', LogisticsStatus), 0
FROM ImportProcesses
WHERE NOT EXISTS (SELECT 1 FROM WorkflowTransitionEvents e WHERE e.AggregateType = 'IMPORT_PROCESS' AND e.AggregateId = ImportProcesses.Id AND e.Version = 0);
