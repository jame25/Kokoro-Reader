using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace KokoroReader.Models
{
    public class BookPositionManager
    {
        private readonly string positionsDirectory;
        private readonly Dictionary<string, BookPosition> positions;

        public BookPositionManager()
        {
            positionsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KokoroReader",
                "positions"
            );
            positions = new Dictionary<string, BookPosition>();
            LoadPositions();
        }

        private void LoadPositions()
        {
            try
            {
                if (!Directory.Exists(positionsDirectory))
                {
                    Directory.CreateDirectory(positionsDirectory);
                    return;
                }

                var positionFiles = Directory.GetFiles(positionsDirectory, "*.json");
                foreach (var file in positionFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var position = JsonSerializer.Deserialize<BookPosition>(json);
                        if (position != null && File.Exists(position.BookPath))
                        {
                            positions[position.BookPath] = position;
                        }
                    }
                    catch
                    {
                        // Skip invalid position files
                    }
                }
            }
            catch
            {
                // Ignore errors during initial load
            }
        }

        public BookPosition? GetPosition(string bookPath)
        {
            return positions.TryGetValue(bookPath, out var position) ? position : null;
        }

        public async Task SavePosition(string bookPath, int chapterIndex, int pageIndex)
        {
            try
            {
                var position = new BookPosition(bookPath, chapterIndex, pageIndex);
                positions[bookPath] = position;

                var filePath = Path.Combine(positionsDirectory, 
                    $"{Path.GetFileNameWithoutExtension(bookPath)}_position.json");

                var json = JsonSerializer.Serialize(position);
                await File.WriteAllTextAsync(filePath, json);

                // Clean up old position files
                await CleanupOldPositions();
            }
            catch
            {
                // Ignore errors during save
            }
        }

        private async Task CleanupOldPositions()
        {
            try
            {
                var positionFiles = Directory.GetFiles(positionsDirectory, "*.json");
                var oldFiles = positionFiles
                    .Select(f => new FileInfo(f))
                    .Where(f => f.LastWriteTime < DateTime.Now.AddMonths(-3))
                    .ToList();

                foreach (var file in oldFiles)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                        // Skip files that can't be deleted
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
} 