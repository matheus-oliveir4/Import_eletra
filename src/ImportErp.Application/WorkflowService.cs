using ImportErp.Domain;

namespace ImportErp.Application;

public interface IWorkflowRepository
{
    Task<string?> GetImporterAsync(WorkflowAggregateType type, Guid id, CancellationToken cancellationToken);
    Task<WorkflowSnapshot?> GetAsync(WorkflowAggregateType type, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkflowTransitionEvent>?> GetHistoryAsync(WorkflowAggregateType type, Guid id, CancellationToken cancellationToken);
    Task<WorkflowTransitionEvent> TransitionAsync(WorkflowAggregateType type, Guid id, long expectedVersion,
        string target, string actor, string reason, IReadOnlyDictionary<string, string?> evidence,
        CancellationToken cancellationToken);
}

public sealed record TransitionWorkflowCommand(
    WorkflowAggregateType AggregateType,
    Guid AggregateId,
    long ExpectedVersion,
    string TargetState,
    string Actor,
    string Reason,
    IReadOnlyDictionary<string, string?> Evidence,
    ErpAccess Access);

public sealed class WorkflowService(IWorkflowRepository repository)
{
    public async Task<WorkflowSnapshot?> GetAsync(WorkflowAggregateType type, Guid id, ErpAccess access, CancellationToken cancellationToken)
    {
        var importer = await repository.GetImporterAsync(type, id, cancellationToken);
        if (importer is null || !access.AllowsImporter(importer)) return null;
        return await repository.GetAsync(type, id, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowTransitionEvent>?> GetHistoryAsync(WorkflowAggregateType type, Guid id, ErpAccess access, CancellationToken cancellationToken)
    {
        var importer = await repository.GetImporterAsync(type, id, cancellationToken);
        if (importer is null || !access.AllowsImporter(importer)) return null;
        return await repository.GetHistoryAsync(type, id, cancellationToken);
    }

    public async Task<WorkflowTransitionEvent> TransitionAsync(TransitionWorkflowCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason)) throw new DomainValidationException("Informe o motivo da transição.");
        if (string.IsNullOrWhiteSpace(command.Actor)) throw new DomainValidationException("A identidade do responsável é obrigatória.");
        if (string.IsNullOrWhiteSpace(command.TargetState)) throw new DomainValidationException("Informe o estado de destino.");
        if (command.Evidence is null) throw new DomainValidationException("Informe as evidências da transição.");
        var importer = await repository.GetImporterAsync(command.AggregateType, command.AggregateId, cancellationToken)
            ?? throw new KeyNotFoundException("Entidade não encontrada.");
        if (!command.Access.AllowsImporter(importer)) throw new KeyNotFoundException("Entidade não encontrada.");
        var current = await repository.GetAsync(command.AggregateType, command.AggregateId, cancellationToken)
            ?? throw new KeyNotFoundException("Workflow não encontrado.");
        if (current.Version != command.ExpectedVersion) throw new ConcurrencyException(command.AggregateId, command.ExpectedVersion, current.Version);

        var decision = WorkflowRules.Evaluate(command.AggregateType, current, command.TargetState, command.Evidence);
        if (!decision.Allowed)
            throw new WorkflowTransitionException(decision.Error ?? "Transição não permitida.", decision.MissingEvidence, decision.RequiredPermission);
        if (decision.RequiredPermission is { } permission && !command.Access.Has(permission))
            throw new UnauthorizedAccessException("O usuário não possui a permissão necessária para esta transição.");

        return await repository.TransitionAsync(command.AggregateType, command.AggregateId,
            command.ExpectedVersion, command.TargetState, command.Actor.Trim(), command.Reason.Trim(), command.Evidence, cancellationToken);
    }
}

public sealed class WorkflowTransitionException(string message, IReadOnlyList<string> missingEvidence, string? requiredPermission)
    : DomainValidationException(message)
{
    public IReadOnlyList<string> MissingEvidence { get; } = missingEvidence;
    public string? RequiredPermission { get; } = requiredPermission;
}
