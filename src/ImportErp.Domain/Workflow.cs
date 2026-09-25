namespace ImportErp.Domain;

public enum WorkflowAggregateType { PurchaseOrder, ImportProcess }

public sealed record WorkflowSnapshot(
    WorkflowAggregateType AggregateType,
    Guid AggregateId,
    string State,
    string? PreviousActiveState,
    long Version);

public sealed record WorkflowTransitionEvent(
    Guid Id,
    WorkflowAggregateType AggregateType,
    Guid AggregateId,
    string FromState,
    string ToState,
    string Actor,
    string Reason,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string?> Evidence,
    long Version);

public sealed record WorkflowTransitionDecision(
    bool Allowed,
    string? RequiredPermission,
    IReadOnlyList<string> MissingEvidence,
    string? Error)
{
    public static WorkflowTransitionDecision Reject(string message) => new(false, null, [], message);
    public static WorkflowTransitionDecision Permit(string permission, params string[] evidence) => new(true, permission, evidence, null);
}

/// <summary>Central transition contract shared by PO and IP commands.</summary>
public static class WorkflowRules
{
    public const string TransitionPermission = "process.transition";
    public const string ReopenPermission = "process.reopen";
    public const string PurchaseOrderTransitionPermission = "purchase-order.transition";
    public const string PurchaseOrderReopenPermission = "purchase-order.reopen";
    public const string CancelPermission = "process.cancel";

    private static readonly HashSet<string> ActiveProcessStates = new(StringComparer.Ordinal)
    { "NOVO", "AGUARDANDO_PO", "EM_PRODUCAO", "PRE_EMBARQUE", "BOOKING", "EMBARCADO", "EM_TRANSITO", "CHEGADA", "DESEMBARACO", "LIBERADO", "ENTREGUE" };
    private static readonly HashSet<string> ActivePoStates = new(StringComparer.Ordinal)
    { "RASCUNHO_INTERNO", "IDENTIFICADA_NO_LEGADO", "AGUARDANDO_APROVACAO", "APROVADA", "ENVIADA", "EM_ATENDIMENTO" };

    public static WorkflowTransitionDecision Evaluate(
        WorkflowAggregateType type,
        WorkflowSnapshot current,
        string target,
        IReadOnlyDictionary<string, string?> evidence)
    {
        var state = Canonical(current.State);
        var next = Canonical(target);
        if (state == next) return WorkflowTransitionDecision.Reject("O estado de destino já é o estado atual.");

        if (type == WorkflowAggregateType.ImportProcess)
        {
            if (next == "CANCELADO" && ActiveProcessStates.Contains(state))
                return Required("process.cancel", evidence, "cancellationConfirmed", "relatedRecordsReviewed");
            if (state == "CANCELADO" || state == "FINALIZADO")
            {
                if (next != "REABRIR") return WorkflowTransitionDecision.Reject("Processo cancelado ou finalizado só pode ser reaberto por comando explícito.");
                return Required(ReopenPermission, evidence, "reopenChecklistApproved");
            }
            if (next == "REABRIR") return WorkflowTransitionDecision.Reject("Somente processo finalizado ou cancelado pode ser reaberto.");
            var permission = TransitionPermission;
            var requirements = (state, next) switch
            {
                ("NOVO", "AGUARDANDO_PO") => new[] { "importer", "supplier", "itemsComplete" },
                ("AGUARDANDO_PO", "EM_PRODUCAO") => new[] { "approvedAndSentPoOrException" },
                ("EM_PRODUCAO", "PRE_EMBARQUE") => new[] { "availabilityConfirmed" },
                ("PRE_EMBARQUE", "BOOKING") => new[] { "transportMode", "route", "estimatedShipmentDate" },
                ("BOOKING", "EMBARCADO") => new[] { "transportDocument", "effectiveShipmentDate", "itemAssociationConfirmed" },
                ("EMBARCADO", "EM_TRANSITO") => new[] { "departureConfirmed" },
                ("EM_TRANSITO", "CHEGADA") => new[] { "arrivalDate" },
                ("CHEGADA", "DESEMBARACO") => new[] { "fiscalOwner", "clearanceOpened" },
                ("DESEMBARACO", "LIBERADO") => new[] { "clearanceDate", "fiscalDocumentOrException" },
                ("LIBERADO", "ENTREGUE") => new[] { "deliveryDate", "receiptConfirmed" },
                ("ENTREGUE", "FINALIZADO") => new[] { "closureChecklistApproved" },
                _ => null
            };
            return requirements is null
                ? WorkflowTransitionDecision.Reject($"Transição logística não permitida: {state} → {next}.")
                : Required(permission, evidence, requirements);
        }

        if (type == WorkflowAggregateType.PurchaseOrder)
        {
            if (next == "CANCELADA" && ActivePoStates.Contains(state))
                return Required("purchase-order.transition", evidence, "cancellationConfirmed", "relatedRecordsReviewed");
            if (state is "CANCELADA" or "CONCLUIDA")
            {
                if (next != "REABRIR") return WorkflowTransitionDecision.Reject("PO concluída ou cancelada só pode ser reaberta por comando explícito.");
                return Required(PurchaseOrderReopenPermission, evidence, "reopenChecklistApproved");
            }
            if (next == "REABRIR") return WorkflowTransitionDecision.Reject("Somente PO concluída ou cancelada pode ser reaberta.");
            var requirements = (state, next) switch
            {
                ("RASCUNHO_INTERNO", "AGUARDANDO_APROVACAO") => new[] { "itemsAndContextComplete" },
                ("IDENTIFICADA_NO_LEGADO", "AGUARDANDO_APROVACAO") => new[] { "officialReferenceOrAuthorizedReview" },
                ("AGUARDANDO_APROVACAO", "APROVADA") => new[] { "approvalEvidence" },
                ("APROVADA", "ENVIADA") => new[] { "sentDate", "communicationEvidence" },
                ("ENVIADA", "EM_ATENDIMENTO") => new[] { "confirmedAllocation" },
                ("EM_ATENDIMENTO", "CONCLUIDA") => new[] { "eligibleBalanceClosed", "closureChecklistApproved" },
                _ => null
            };
            return requirements is null
                ? WorkflowTransitionDecision.Reject($"Transição comercial não permitida: {state} → {next}.")
                : Required(PurchaseOrderTransitionPermission, evidence, requirements);
        }

        return WorkflowTransitionDecision.Reject("Tipo de entidade de workflow desconhecido.");
    }

    public static string InitialState(WorkflowAggregateType type, string? legacyStatus = null)
    {
        if (type == WorkflowAggregateType.PurchaseOrder) return "IDENTIFICADA_NO_LEGADO";
        if (string.IsNullOrWhiteSpace(legacyStatus)) return "INDETERMINADO";
        var status = Canonical(legacyStatus ?? "");
        return status switch
        {
            "WAITING_PRODUCTION" => "EM_PRODUCAO",
            "WAITING_SHIPMENT" => "PRE_EMBARQUE",
            "WAITING_ARRIVAL" => "EM_TRANSITO",
            "CUSTOMS_CLEARANCE" => "DESEMBARACO",
            "DELIVERED" => "ENTREGUE",
            "CANCELLED" => "CANCELADO",
            "NOVO" or "AGUARDANDO_PO" or "EM_PRODUCAO" or "PRE_EMBARQUE" or "BOOKING" or "EMBARCADO" or "EM_TRANSITO" or "CHEGADA" or "DESEMBARACO" or "LIBERADO" or "ENTREGUE" or "FINALIZADO" or "CANCELADO" => status,
            _ => "INDETERMINADO"
        };
    }

    public static string Canonical(string value) => value.Trim().Replace(' ', '_').ToUpperInvariant();

    private static WorkflowTransitionDecision Required(string permission, IReadOnlyDictionary<string, string?> evidence, params string[] keys)
    {
        var missing = keys.Where(key => !evidence.TryGetValue(key, out var value) || !IsValidEvidence(key, value)).ToArray();
        return missing.Length == 0
            ? WorkflowTransitionDecision.Permit(permission)
            : new WorkflowTransitionDecision(false, permission, missing, "Faltam evidências obrigatórias para a transição.");
    }

    private static bool IsValidEvidence(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var booleanKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "itemsComplete", "approvedAndSentPoOrException", "availabilityConfirmed", "itemAssociationConfirmed",
            "departureConfirmed", "clearanceOpened", "fiscalDocumentOrException", "receiptConfirmed",
            "closureChecklistApproved", "eligibleBalanceClosed", "cancellationConfirmed", "relatedRecordsReviewed",
            "itemsAndContextComplete", "officialReferenceOrAuthorizedReview", "confirmedAllocation", "reopenChecklistApproved"
        };
        if (booleanKeys.Contains(key)) return bool.TryParse(value, out var confirmed) && confirmed;
        if (key.EndsWith("Date", StringComparison.Ordinal)) return DateOnly.TryParse(value, out _);
        return true;
    }
}
