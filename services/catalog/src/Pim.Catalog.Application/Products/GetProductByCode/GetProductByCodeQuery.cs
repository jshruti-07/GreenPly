using MediatR;
using Pim.SharedKernel.Results;

namespace Pim.Catalog.Application.Products.GetProductByCode;

public sealed record GetProductByCodeQuery(string Code) : IRequest<Result<ProductDto>>;
