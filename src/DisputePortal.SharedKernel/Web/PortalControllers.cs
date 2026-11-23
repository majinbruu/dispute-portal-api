using System.Reflection;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DisputePortal.SharedKernel.Web;

/// <summary>Registers MVC controllers the way every module expects them to behave.</summary>
public static class PortalControllers
{
    public const string ValidationFailedCode = "validation_failed";

    /// <summary>
    /// Adds controllers from the given module assemblies. Module controllers are internal, like everything else
    /// in a module, so a feature provider is added that lets MVC discover them.
    /// </summary>
    public static IMvcBuilder AddPortalControllers(this IServiceCollection services, params Assembly[] moduleAssemblies)
    {
        var mvc = services
            .AddControllers(options => options.Filters.Add<RequestValidationFilter>())
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new InternalControllerFeatureProvider()))
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = CreateValidationResponse);

        foreach (var assembly in moduleAssemblies)
        {
            mvc.AddApplicationPart(assembly);
        }

        return mvc;
    }

    /// <summary>One shape for every invalid request, whether model binding or a validator rejected it.</summary>
    private static BadRequestObjectResult CreateValidationResponse(ActionContext context)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(
            context.HttpContext,
            context.ModelState,
            StatusCodes.Status400BadRequest,
            title: "The request is invalid.",
            detail: "One or more fields are invalid.");
        problem.Extensions["code"] = ValidationFailedCode;

        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }
}

/// <summary>MVC only discovers public controllers by default. This also accepts the internal ones modules define.</summary>
internal sealed class InternalControllerFeatureProvider : ControllerFeatureProvider
{
    protected override bool IsController(TypeInfo typeInfo) =>
        typeInfo is { IsClass: true, IsAbstract: false, IsPublic: false, ContainsGenericParameters: false }
        && typeof(ControllerBase).IsAssignableFrom(typeInfo);
}

/// <summary>Runs the registered FluentValidation validator for each action argument before the action executes.</summary>
internal sealed class RequestValidationFilter(IOptions<ApiBehaviorOptions> apiBehavior) : IAsyncActionFilter
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
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var failure in result.Errors)
            {
                // Field names in the response match the JSON and query string names the client sent, which are camelCase.
                context.ModelState.AddModelError(ToCamelCase(failure.PropertyName), failure.ErrorMessage);
            }
        }

        if (!context.ModelState.IsValid)
        {
            context.Result = apiBehavior.Value.InvalidModelStateResponseFactory(context);
            return;
        }

        await next();
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
