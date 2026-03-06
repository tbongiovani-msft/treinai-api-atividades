using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TreinAI.Api.Atividades.Validators;
using TreinAI.Shared.Exceptions;
using TreinAI.Shared.Middleware;
using TreinAI.Shared.Models;
using TreinAI.Shared.Repositories;
using TreinAI.Shared.Validation;

namespace TreinAI.Api.Atividades.Functions;

/// <summary>
/// CRUD for Atividade (workout execution / activity log).
/// Students log their workouts; professors can view them.
/// </summary>
public class AtividadeFunctions
{
    private readonly IRepository<Atividade> _repository;
    private readonly IRepository<Aluno> _alunoRepo;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<AtividadeFunctions> _logger;

    public AtividadeFunctions(
        IRepository<Atividade> repository,
        IRepository<Aluno> alunoRepo,
        TenantContext tenantContext,
        ILogger<AtividadeFunctions> logger)
    {
        _repository = repository;
        _alunoRepo = alunoRepo;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    private async Task<string?> ResolveAlunoRecordIdAsync()
    {
        var alunos = await _alunoRepo.QueryAsync(
            _tenantContext.TenantId, a => a.UserId == _tenantContext.UserId);
        return alunos.FirstOrDefault()?.Id;
    }

    /// <summary>
    /// GET /api/atividades — List activities.
    /// Supports ?alunoId= and ?treinoId= filters.
    /// </summary>
    [Function("GetAtividades")]
    public async Task<HttpResponseData> GetAtividades(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "atividades")] HttpRequestData req)
    {
        var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var alunoId = queryParams["alunoId"];
        var treinoId = queryParams["treinoId"];

        IReadOnlyList<Atividade> atividades;

        if (!string.IsNullOrEmpty(treinoId))
        {
            atividades = await _repository.QueryAsync(
                _tenantContext.TenantId,
                a => a.TreinoId == treinoId);
        }
        else if (!string.IsNullOrEmpty(alunoId))
        {
            atividades = await _repository.QueryAsync(
                _tenantContext.TenantId,
                a => a.AlunoId == alunoId);
        }
        else if (_tenantContext.IsAluno)
        {
            var alunoRecordId = await ResolveAlunoRecordIdAsync();
            atividades = alunoRecordId != null
                ? await _repository.QueryAsync(_tenantContext.TenantId, a => a.AlunoId == alunoRecordId)
                : Array.Empty<Atividade>();
        }
        else
        {
            atividades = await _repository.GetAllAsync(_tenantContext.TenantId);
        }

        return await ValidationHelper.OkAsync(req, atividades);
    }

    /// <summary>
    /// GET /api/atividades/{id}
    /// </summary>
    [Function("GetAtividadeById")]
    public async Task<HttpResponseData> GetAtividadeById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "atividades/{id}")] HttpRequestData req,
        string id)
    {
        var atividade = await _repository.GetByIdAsync(id, _tenantContext.TenantId);
        if (atividade == null)
            throw new NotFoundException("Atividade", id);

        if (_tenantContext.IsAluno)
        {
            var alunoRecordId = await ResolveAlunoRecordIdAsync();
            if (atividade.AlunoId != alunoRecordId)
                throw new ForbiddenException("Você só pode acessar suas próprias atividades.");
        }

        return await ValidationHelper.OkAsync(req, atividade);
    }

    /// <summary>
    /// POST /api/atividades — Log a new workout activity.
    /// Primarily used by students.
    /// </summary>
    [Function("CreateAtividade")]
    public async Task<HttpResponseData> CreateAtividade(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "atividades")] HttpRequestData req)
    {
        var validator = new AtividadeValidator();
        var atividade = await ValidationHelper.ValidateRequestAsync(req, validator);

        // Alunos can only create activities for themselves
        if (_tenantContext.IsAluno)
        {
            var alunoRecordId = await ResolveAlunoRecordIdAsync();
            atividade.AlunoId = alunoRecordId ?? _tenantContext.UserId;
        }

        atividade.TenantId = _tenantContext.TenantId;
        atividade.CreatedBy = _tenantContext.UserId;
        atividade.UpdatedBy = _tenantContext.UserId;

        _logger.LogInformation("Creating atividade for aluno {AlunoId}, treino {TreinoId}",
            atividade.AlunoId, atividade.TreinoId);

        var created = await _repository.CreateAsync(atividade);
        return await ValidationHelper.CreatedAsync(req, created);
    }

    /// <summary>
    /// PUT /api/atividades/{id} — Update activity (e.g., add more sets/reps).
    /// </summary>
    [Function("UpdateAtividade")]
    public async Task<HttpResponseData> UpdateAtividade(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "atividades/{id}")] HttpRequestData req,
        string id)
    {
        var existing = await _repository.GetByIdAsync(id, _tenantContext.TenantId);
        if (existing == null)
            throw new NotFoundException("Atividade", id);

        if (_tenantContext.IsAluno)
        {
            var alunoRecordId = await ResolveAlunoRecordIdAsync();
            if (existing.AlunoId != alunoRecordId)
                throw new ForbiddenException("Você só pode editar suas próprias atividades.");
        }

        var validator = new AtividadeValidator();
        var atividade = await ValidationHelper.ValidateRequestAsync(req, validator);

        atividade.Id = id;
        atividade.TenantId = _tenantContext.TenantId;
        atividade.CreatedAt = existing.CreatedAt;
        atividade.CreatedBy = existing.CreatedBy;
        atividade.UpdatedBy = _tenantContext.UserId;

        var updated = await _repository.UpdateAsync(atividade);
        return await ValidationHelper.OkAsync(req, updated);
    }

    /// <summary>
    /// DELETE /api/atividades/{id}
    /// </summary>
    [Function("DeleteAtividade")]
    public async Task<HttpResponseData> DeleteAtividade(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "atividades/{id}")] HttpRequestData req,
        string id)
    {
        var existing = await _repository.GetByIdAsync(id, _tenantContext.TenantId);
        if (existing == null)
            throw new NotFoundException("Atividade", id);

        if (_tenantContext.IsAluno)
        {
            var alunoRecordId = await ResolveAlunoRecordIdAsync();
            if (existing.AlunoId != alunoRecordId)
                throw new ForbiddenException("Você só pode excluir suas próprias atividades.");
        }

        await _repository.DeleteAsync(id, _tenantContext.TenantId);
        return ValidationHelper.NoContent(req);
    }

    /// <summary>
    /// GET /api/alunos/{alunoId}/atividades — Get all activities for a student.
    /// </summary>
    [Function("GetAtividadesByAluno")]
    public async Task<HttpResponseData> GetAtividadesByAluno(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "alunos/{alunoId}/atividades")] HttpRequestData req,
        string alunoId)
    {
        if (_tenantContext.IsAluno)
        {
            var alunoRecordId = await ResolveAlunoRecordIdAsync();
            if (alunoId != alunoRecordId)
                throw new ForbiddenException("Você só pode acessar suas próprias atividades.");
        }

        var atividades = await _repository.QueryAsync(
            _tenantContext.TenantId,
            a => a.AlunoId == alunoId);

        return await ValidationHelper.OkAsync(req, atividades);
    }
}
