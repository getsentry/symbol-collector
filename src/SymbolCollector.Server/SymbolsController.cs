using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace SymbolCollector.Server
{
    [Route("/image")]
    public class SymbolsController : Controller
    {
        private readonly long _fileSizeLimit;
        private readonly ILogger<SymbolsController> _logger;
        private readonly char[] _invalidChars;

        private static readonly FormOptions DefaultFormOptions = new FormOptions();

        public SymbolsController(ILogger<SymbolsController> logger, IConfiguration config)
        {
            _logger = logger;
            _fileSizeLimit = config.GetValue<long>("FileSizeLimitBytes");
            // Don't allow file names with paths encoded.
            _invalidChars = Path.GetInvalidFileNameChars().Concat(new[] {'/', '\\'}).ToArray();
        }

        [HttpPut]
        public async Task<IActionResult> UploadSymbol()
        {
            if (!MultipartRequestHelper.IsMultipartContentType(Request.ContentType))
            {
                return BadRequest(ModelState);
            }

            // Accumulate the form data key-value pairs in the request (formAccumulator).
            var formAccumulator = new KeyValueAccumulator();

            var boundary = MultipartRequestHelper.GetBoundary(
                MediaTypeHeaderValue.Parse(Request.ContentType),
                DefaultFormOptions.MultipartBoundaryLengthLimit);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var section = await reader.ReadNextSectionAsync();

            while (section != null)
            {
                if (ContentDispositionHeaderValue.TryParse(
                    section.ContentDisposition, out var contentDisposition) && MultipartRequestHelper
                    .HasFileContentDisposition(contentDisposition))
                {
                    var file = await ProcessStreamedFile(section, contentDisposition, ModelState, _fileSizeLimit);

                    if (!ModelState.IsValid)
                    {
                        return BadRequest(ModelState);
                    }
                }

                // Drain any remaining section body that hasn't been consumed and
                // read the headers for the next section.
                section = await reader.ReadNextSectionAsync();
            }

            var formData = new FormData();
            var formValueProvider = new FormValueProvider(
                BindingSource.Form,
                new FormCollection(formAccumulator.GetResults()),
                CultureInfo.CurrentCulture);
            var bindingSuccessful = await TryUpdateModelAsync(formData, prefix: "",
                valueProvider: formValueProvider);

            if (!bindingSuccessful)
            {
                ModelState.AddModelError("File",
                    "The request couldn't be processed (Error 5).");
                // Log error

                return BadRequest(ModelState);
            }

            // **WARNING!**
            // In the following example, the file is saved without
            // scanning the file's contents. In most production
            // scenarios, an anti-virus/anti-malware scanner API
            // is used on the file before making the file available
            // for download or for use by other systems.
            // For more information, see the topic that accompanies
            // this sample app.


            // TODO: Do the magic
//            var file = new AppFile()
//            {
//                Content = streamedFileContent,
//                UntrustedName = untrustedFileNameForStorage,
//                Note = formData.Note,
//                Size = streamedFileContent.Length,
//                UploadDT = DateTime.UtcNow
//            };
//
//            _context.File.Add(file);
//            await _context.SaveChangesAsync();

            return Created(nameof(SymbolsController), null);
        }

        public class FormData
        {
            public string? Note { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> UploadPhysical()
        {
            if (!MultipartRequestHelper.IsMultipartContentType(Request.ContentType))
            {
                ModelState.AddModelError("File",
                    $"The request couldn't be processed (Error 1).");
                // Log error

                return BadRequest(ModelState);
            }

            var boundary = MultipartRequestHelper.GetBoundary(
                MediaTypeHeaderValue.Parse(Request.ContentType),
                DefaultFormOptions.MultipartBoundaryLengthLimit);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);
            var section = await reader.ReadNextSectionAsync();

            while (section != null)
            {
                var hasContentDispositionHeader =
                    ContentDispositionHeaderValue.TryParse(
                        section.ContentDisposition, out var contentDisposition);

                if (hasContentDispositionHeader)
                {
                    if (!MultipartRequestHelper
                        .HasFileContentDisposition(contentDisposition))
                    {
                        ModelState.AddModelError("File",
                            $"The request couldn't be processed (Error 2).");
                        // Log error

                        return BadRequest(ModelState);
                    }

                    var file = await ProcessStreamedFile(
                        section, contentDisposition, ModelState, _fileSizeLimit);

                    if (!ModelState.IsValid)
                    {
                        return BadRequest(ModelState);
                    }

//                    await using var targetStream = System.IO.File.Create(
//                        Path.Combine(_targetFilePath, trustedFileNameForFileStorage));
//                    await targetStream.WriteAsync(streamedFileContent);
//
//                    _logger.LogInformation(
//                        "Uploaded file '{TrustedFileNameForDisplay}' saved to " +
//                        "'{TargetFilePath}' as {TrustedFileNameForFileStorage}",
//                        trustedFileNameForDisplay, _targetFilePath,
//                        trustedFileNameForFileStorage);
                }

                section = await reader.ReadNextSectionAsync();
            }

            return Created(nameof(SymbolsController), null);
        }

        private static Encoding GetEncoding(MultipartSection section)
        {
            var hasMediaTypeHeader =
                MediaTypeHeaderValue.TryParse(section.ContentType, out var mediaType);

            // UTF-7 is insecure and shouldn't be honored. UTF-8 succeeds in
            // most cases.
            if (!hasMediaTypeHeader || Encoding.UTF7.Equals(mediaType.Encoding))
            {
                return Encoding.UTF8;
            }

            return mediaType.Encoding;
        }

        public async ValueTask<(string fileName, byte[] data)> ProcessStreamedFile(
            MultipartSection section, ContentDispositionHeaderValue contentDisposition,
            ModelStateDictionary modelState, long sizeLimit)
        {
            try
            {
                await using var memoryStream = new MemoryStream();
                await section.Body.CopyToAsync(memoryStream);

                var fileName = contentDisposition.FileName.Value;
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
                    return (fileName, memoryStream.ToArray());
                }
            }
            catch (Exception ex)
            {
                modelState.AddModelError("File",
                    "The upload failed. Please contact the Help Desk " +
                    $" for support. Error: {ex.HResult}");
                // Log the exception
            }

            return ("error", new byte[0]);
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

        public static bool HasFormDataContentDisposition(ContentDispositionHeaderValue contentDisposition)
        {
            // Content-Disposition: form-data; name="key";
            return contentDisposition != null
                   && contentDisposition.DispositionType.Equals("form-data")
                   && string.IsNullOrEmpty(contentDisposition.FileName.Value)
                   && string.IsNullOrEmpty(contentDisposition.FileNameStar.Value);
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
