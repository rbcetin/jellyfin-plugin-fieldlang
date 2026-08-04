using System.Net.Mime;
using Jellyfin.Plugin.FieldLang.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.FieldLang.Api;

/// <summary>
/// Feeds the configuration page the data it needs to render itself.
/// </summary>
/// <remarks>
/// The config page is generic -- it draws a control for every library crossed with every field the
/// catalog supports. Rather than hardcoding that grid in JavaScript, it asks the server, so adding
/// a field to <see cref="FieldCatalog"/> updates the UI with no page edit.
/// </remarks>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("FieldLang")]
[Produces(MediaTypeNames.Application.Json)]
public class FieldLangController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>Initializes a new instance of the <see cref="FieldLangController"/> class.</summary>
    /// <param name="libraryManager">Library manager.</param>
    public FieldLangController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Gets the libraries and the field catalog.
    /// </summary>
    /// <response code="200">Schema returned.</response>
    /// <returns>The schema the config page renders from.</returns>
    [HttpGet("Schema")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetSchema()
    {
        var libraries = _libraryManager.GetVirtualFolders()
            .Select(f => new
            {
                Id = f.ItemId,
                Name = f.Name,
                CollectionType = f.CollectionType?.ToString(),
            })
            .ToList();

        var itemTypes = FieldCatalog.ItemTypes
            .Select(t => new
            {
                Name = t,
                Fields = FieldCatalog.ByItemType[t]
                    .Select(f => new { f.Name, f.Label, Lockable = f.LockField.HasValue })
                    .ToList(),
            })
            .ToList();

        return Ok(new { Libraries = libraries, ItemTypes = itemTypes });
    }
}
