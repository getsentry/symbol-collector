using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using SymbolCollector.Core;

namespace SymbolCollector.Server
{
    public class BatchStartRequestModel
    {
        [Required]
        [StringLength(1000, ErrorMessage = "A batch friendly name can't be longer than 1000 characters.")]
        [Display(Name = "Batch friendly name")]
        public string BatchFriendlyName { get; set; } = default!; // model validation

        [JsonConverter(typeof(JsonStringEnumConverter))]
        [Range((int)BatchType.WatchOS, (int)BatchType.Android)]
        public BatchType BatchType { get; set; }
    }

    public class BatchEndRequestModel
    {
        public ClientMetricsModel? ClientMetrics { get; set; }
    }

    public class ClientMetricsModel : IClientMetrics
    {
        public DateTimeOffset StartedTime { get; set; }
        public long FilesProcessedCount { get; set; }
        public long BatchesProcessedCount { get; set; }
        public long JobsInFlightCount { get; set; }
        public long FailedToUploadCount { get; set; }
        public long SuccessfullyUploadCount { get; set; }
        public long AlreadyExistedCount { get; set; }
        public long MachOFileFoundCount { get; set; }
        public long ElfFileFoundCount { get; set; }
        public int FatMachOFileFoundCount { get; set; }
        public long UploadedBytesCount { get; set; }
        public int DirectoryUnauthorizedAccessCount { get; set; }
        public int DirectoryDoesNotExistCount { get; set; }
    }


    [Route(Route)]
    public class SymbolsController : Controller
    {
        public const string Route = "/symbol";

        private readonly long _fileSizeLimit;
        private readonly ISymbolService _symbolService;
        private readonly ILogger<SymbolsController> _logger;
        private readonly char[] _invalidChars;

        private static readonly FormOptions DefaultFormOptions = new FormOptions();

        public SymbolsController(
            IConfiguration config,
            ISymbolService symbolService,
            ILogger<SymbolsController> logger)
        {
            _symbolService = symbolService;
            _logger = logger;
            _fileSizeLimit = config.GetValue<long>("FileSizeLimitBytes");
            // Don't allow file names with paths encoded.
            _invalidChars = Path.GetInvalidFileNameChars().Concat(new[] {'/', '\\'}).ToArray();
        }

        [HttpGet(Route + "/batch/{batchId}")]
        public async Task<SymbolUploadBatch?> Get([FromRoute] Guid batchId, CancellationToken token)
        {
            var batch = await _symbolService.GetBatch(batchId, token);
            return batch;
        }

        [HttpPost(Route + "/batch/{batchId}/start")]
        public async Task<IActionResult> Start(
            [FromRoute] Guid batchId,
            [FromBody] BatchStartRequestModel model,
            CancellationToken token)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (await _symbolService.GetBatch(batchId, token) is {})
            {
                return BadRequest($"Batch Id {batchId} was already used.");
            }

            await _symbolService.Start(batchId, model.BatchFriendlyName, model.BatchType, token);

            return Ok();
        }

        [HttpPost(Route + "/batch/{batchId}/close")]
        public async Task<IActionResult> CloseBatch([FromRoute] Guid batchId, [FromBody] BatchEndRequestModel model,
            CancellationToken token)
        {
            await ValidateBatch(batchId, ModelState, token);

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            await _symbolService.Finish(batchId, model.ClientMetrics, token);
            return NoContent();
        }

        [HttpHead(Route + "/batch/{batchId}/check/{debugId}/{hash}")]
        public async Task<IActionResult> IsSymbolMissing(
            [FromRoute] Guid batchId,
            [FromRoute] string debugId,
            [FromRoute] string hash,
            CancellationToken token)
        {
            await ValidateBatch(batchId, ModelState, token);

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var symbol = await _symbolService.GetSymbol(debugId, token);
            if (symbol is null)
            {
                _logger.LogDebug("{batchId} looked for {debugId} and {hash} which is a missing symbol.",
                    batchId, debugId, hash);
                return Ok();
            }

            if (hash is {} && symbol.Hash is {} && string.CompareOrdinal(hash, symbol.Hash) != 0)
            {
                using (_logger.BeginScope(new Dictionary<string, string>()
                {
                    {"existing-file-hash", symbol.Hash},
                    {"existing-file-name", symbol.Name},
                    {"new-file-hash", hash},
                }))
                {
                    // Return OK so that client uploads the symbol. The upload handing code
                    // will take the file "aside" for troubleshooting
                    _logger.LogWarning(
                        "File with {debugId} as part of {batchId} has a conflicting hash with the existing file.",
                        debugId, batchId);
                }

                return Ok();
            }

            if (!symbol.BatchIds.Contains(batchId))
            {
                await _symbolService.Relate(batchId, symbol, token);
            }

            return Conflict();
        }

        [HttpPost(Route + "/batch/{batchId}/upload/")]
        [DisableFormValueModelBinding]
        public async Task<IActionResult> UploadSymbol(
            [FromRoute] Guid batchId,
            CancellationToken token)
        {
            await ValidateBatch(batchId, ModelState, token);

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (!MultipartRequestHelper.IsMultipartContentType(Request.ContentType))
            {
                return BadRequest($"ContentType {Request.ContentType} is not supported.");
            }

            var boundary = MultipartRequestHelper.GetBoundary(
                MediaTypeHeaderValue.Parse(Request.ContentType),
                DefaultFormOptions.MultipartBoundaryLengthLimit);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var section = await reader.ReadNextSectionAsync(token);

            if (section == null)
            {
                return BadRequest("No file received.");
            }

            var sectionsCount = 0;
            var invalidContentDispositions = new List<string>();
            var results = new List<(string FileName, StoreResult Result)>();
            while (section != null)
            {
                sectionsCount++;
                if (ContentDispositionHeaderValue.TryParse(
                        section.ContentDisposition, out var contentDisposition) && MultipartRequestHelper
                        .HasFileContentDisposition(contentDisposition))
                {
                    var (fileName, data) =
                        await ProcessStreamedFile(section, contentDisposition, ModelState, _fileSizeLimit);

                    if (!ModelState.IsValid)
                    {
                        return BadRequest(ModelState);
                    }

                    _logger.LogInformation(
                        "Persisting file '{fileName}' with {bytes} bytes.", fileName, data.Length);

                    // TODO: Process the image: do we have it already? is it valid?

                    results.Add((fileName,
                        await _symbolService.Store(
                            batchId,
                            fileName,
                            data,
                            token)));

                    await data.DisposeAsync();
                }
                else
                {
                    invalidContentDispositions.Add(section.ContentDisposition);
                    _logger.LogWarning("ContentDisposition not supported: {contentDisposition}",
                        section.ContentDisposition);
                }

                section = await reader.ReadNextSectionAsync(token);
            }

            var filesCreated = results.Count(r => r.Result == StoreResult.Created);

            var response = new {filesCreated, results};
            return filesCreated switch
            {
                0 when results.Any(p => p.Result == StoreResult.AlreadyExisted)
                => StatusCode(208, response),
                0 => BadRequest(new
                {
                    errorMessage = "Invalid request. No file accepted.",
                    numberOfSectionsReceived = sectionsCount,
                    invalidContentDispositions,
                    response
                }),
                _ => Created(nameof(SymbolsController), response)
            };
        }

        public async Task ValidateBatch(Guid batchId, ModelStateDictionary modelState, CancellationToken token)
        {
            if (batchId == default)
            {
                modelState.AddModelError("Batch", "A BatchId is required.");
            }
            else
            {
                var batch = await _symbolService.GetBatch(batchId, token);
                if (batch is null)
                {
                    modelState.AddModelError("Batch", $"Batch Id {batchId} does not exist.");
                }
                else if (batch.IsClosed)
                {
                    modelState.AddModelError("Batch", $"Batch Id {batchId} is already closed.");
                }
            }
        }

        public async ValueTask<(string fileName, Stream data)> ProcessStreamedFile(
            MultipartSection section, ContentDispositionHeaderValue contentDisposition,
            ModelStateDictionary modelState, long sizeLimit)
        {
            var memoryStream = new MemoryStream();
            string? fileName = null;
            try
            {
                fileName = contentDisposition.FileName.Value;
                await section.Body.CopyToAsync(memoryStream);
                memoryStream.Position = 0; // TODO: needs rewinding?

                if (memoryStream.Length == 0)
                {
                    modelState.AddModelError("File", "The file is empty.");
                }
                else if (memoryStream.Length > sizeLimit)
                {
                    var megabyteSizeLimit = sizeLimit / 1048576;
                    modelState.AddModelError("File", $"The file exceeds {megabyteSizeLimit:N1} MB.");
                    _logger.LogWarning("File name {fileName} is too large: {size} MB.", fileName, megabyteSizeLimit);
                }
                else if (fileName.Any(p => _invalidChars.Contains(p)))
                {
                    ModelState.AddModelError("File", "The file name specified contain invalid characters.");
                    _logger.LogWarning("File name {fileName} contain invalid characters.", fileName);
                }
                else
                {
                    return (fileName, memoryStream);
                }
            }
            catch (Exception ex)
            {
                modelState.AddModelError("File", $"The upload failed. Error: {ex.HResult}");
                _logger.LogError(ex, "Failed to process file {file}", fileName);
            }

            return ("error", memoryStream);
        }
    }

    // Copy pasta from: https://github.com/aspnet/AspNetCore.Docs/blob/master/aspnetcore/mvc/models/file-uploads/samples/2.x/SampleApp/Utilities/MultipartRequestHelper.cs
    public static class MultipartRequestHelper
    {
        // Content-Type: multipart/form-data; boundary="----WebKitFormBoundarymx2fSWqWSd0OxQqq"
        // The spec at https://tools.ietf.org/html/rfc2046#section-5.1 states that 70 characters is a reasonable limit.
        public static string GetBoundary(MediaTypeHeaderValue contentType, int lengthLimit)
        {
            var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;

            if (string.IsNullOrWhiteSpace(boundary))
            {
                throw new InvalidDataException("Missing content-type boundary.");
            }

            if (boundary.Length > lengthLimit)
            {
                throw new InvalidDataException(
                    $"Multipart boundary length limit {lengthLimit} exceeded.");
            }

            return boundary;
        }

        public static bool IsMultipartContentType(string contentType)
        {
            return !string.IsNullOrEmpty(contentType)
                   && contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool HasFileContentDisposition(ContentDispositionHeaderValue contentDisposition)
        {
            // Content-Disposition: form-data; name="myfile1"; filename="Misc 002.jpg"
            return contentDisposition != null
                   && contentDisposition.DispositionType.Equals("form-data")
                   && (!string.IsNullOrEmpty(contentDisposition.FileName.Value)
                       || !string.IsNullOrEmpty(contentDisposition.FileNameStar.Value));
        }
    }
}
