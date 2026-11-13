using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Taskel.Api.Attachments;

/// <summary>
/// For an action that reads an upload itself, as a stream. Stops the framework from reading the
/// whole form first to look for parameters in it, which would load the file into memory or onto
/// disk before the action could check its size.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StreamedUploadAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
