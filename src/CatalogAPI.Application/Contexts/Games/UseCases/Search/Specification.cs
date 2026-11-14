using Flunt.Notifications;
using Flunt.Validations;
using System;
using System.Collections.Generic;
using System.Text;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Search
{
    public class Specification
    {
        private const int MaxTermLength = 100;
        private const int MaxPageSize = 50;

        public static Contract<Notification> Ensure(Request request)
        {
            var hasTerm = !string.IsNullOrWhiteSpace(request.Term);
            var hasFilter = request.Genre is not null
                            || request.MinPrice is not null
                            || request.MaxPrice is not null;

            return new Contract<Notification>()
                .Requires()

                // Precisa de pelo menos um critério, senão vira listagem sem ordem
                .IsTrue(
                    hasTerm || hasFilter,
                    "Term",
                    "Informe um termo de busca ou ao menos um filtro.")

                // Termo
                .IsTrue(
                    request.Term is null || request.Term.Trim().Length <= MaxTermLength,
                    "Term",
                    $"O termo deve ter no máximo {MaxTermLength} caracteres.")

                // Gênero (um número fora do enum passa no binding e não casaria com nada)
                .IsTrue(
                    request.Genre is null || Enum.IsDefined(request.Genre.Value),
                    "Genre",
                    "Gênero inválido.")

                // Preço
                .IsTrue(
                    request.MinPrice is null || request.MinPrice >= 0,
                    "MinPrice",
                    "O preço mínimo não pode ser negativo.")
                .IsTrue(
                    request.MaxPrice is null || request.MaxPrice >= 0,
                    "MaxPrice",
                    "O preço máximo não pode ser negativo.")
                .IsTrue(
                    request.MinPrice is null || request.MaxPrice is null || request.MinPrice <= request.MaxPrice,
                    "MinPrice",
                    "O preço mínimo não pode ser maior que o máximo.")

                // Paginação
                .IsTrue(
                    request.Page >= 1,
                    "Page",
                    "A página deve ser maior ou igual a 1.")
                .IsTrue(
                    request.Size >= 1 && request.Size <= MaxPageSize,
                    "Size",
                    $"O tamanho da página deve estar entre 1 e {MaxPageSize}.");
        }
    }
}
