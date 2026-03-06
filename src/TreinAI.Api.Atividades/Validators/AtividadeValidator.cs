using FluentValidation;
using TreinAI.Shared.Models;

namespace TreinAI.Api.Atividades.Validators;

public class AtividadeValidator : AbstractValidator<Atividade>
{
    public AtividadeValidator()
    {
        RuleFor(x => x.AlunoId)
            .NotEmpty().WithMessage("AlunoId é obrigatório.");

        RuleFor(x => x.TreinoId)
            .NotEmpty().WithMessage("TreinoId é obrigatório.");

        RuleFor(x => x.DivisaoNome)
            .NotEmpty().WithMessage("DivisaoNome é obrigatório.");

        RuleFor(x => x.DataExecucao)
            .NotEmpty().WithMessage("Data de execução é obrigatória.");

        RuleFor(x => x.DuracaoMinutos)
            .GreaterThanOrEqualTo(0).WithMessage("Duração deve ser >= 0.");

        RuleForEach(x => x.ExerciciosExecutados).ChildRules(e =>
        {
            e.RuleFor(x => x.ExercicioId).NotEmpty().WithMessage("ExercicioId é obrigatório.");
            e.RuleFor(x => x.Nome).NotEmpty().WithMessage("Nome do exercício é obrigatório.");
        });
    }
}
