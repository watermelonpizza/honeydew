using System;
using System.Threading;
using System.Threading.Tasks;
using Honeydew.Data;
using Microsoft.Extensions.Options;
using static Nanoid.Nanoid;

namespace Honeydew
{
    public class SlugGenerator
    {
        private readonly ApplicationDbContext _context;
        private readonly SlugOptions _slugOptions;

        public SlugGenerator(IOptionsMonitor<SlugOptions> configuration, ApplicationDbContext context)
        {
            _context = context;
            _slugOptions = configuration.CurrentValue;
        }

        public async Task<string> GenerateSlug(CancellationToken cancellationToken)
        {
            var tries = 0;
            string slug;

            do
            {
                // Generate a few at the current length then increase if still trying
                // async doesn't do anything in this library. Just adds overhead here.
                // ReSharper disable once MethodHasAsyncOverload
                slug = Generate(_slugOptions.Alphabet, _slugOptions.Size + Math.Max(0, tries - 5));

                // TODO: Issue with multiple uploads with the same key at the same time will cause one to error out (both save to upload staging table
                var existingEntry = await _context.Uploads.FindAsync(new[] { slug }, cancellationToken);

                // ReSharper disable once InvertIf
                if (existingEntry != null)
                {
                    slug = null;
                    tries++;
                }
            } while (slug == null && tries < 10);

            if (slug == null)
            {
                throw new Exception("Could not generate a unique slug after 5 tries. Increase the base slug generation length.");
            }

            return slug;
        }
    }

    public class SlugOptions
    {
        public string Alphabet { get; set; } = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        public int Size { get; set; } = 5;
    }
}
