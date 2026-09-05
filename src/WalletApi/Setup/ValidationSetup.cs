using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HiWallet.WalletApi.Setup;

/// <summary>
/// FluentValidation, sınırda (baseline.md madde 6). DataAnnotations YOK (CLAUDE.md).
/// </summary>
public static class ValidationSetup
{
    public static IServiceCollection AddHiWalletValidation(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly(), includeInternalTypes: true);
        services.AddScoped<ValidationFilter>();

        return services;
    }
}

/// <summary>
/// Validator'ı olan her action argümanını doğrular. Filtre olarak yazılıyor ki her
/// controller action'ı aynı üç satırı tekrarlamasın ve biri unutulduğunda doğrulama
/// sessizce atlanmasın.
///
/// FluentValidation'ın kendi auto-validation entegrasyonu kullanılmıyor: 11.x'te
/// deprecated ve MVC'nin ModelState'ine karışıyor.
/// </summary>
internal sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument), context.HttpContext.RequestAborted);

            if (result.IsValid)
            {
                continue;
            }

            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "İstek geçersiz",
                Type = "https://hiwallet.dev/problems/validation"
            });

            return;
        }

        await next();
    }
}
