using Microsoft.AspNetCore.Mvc;
using BlazorApp4.Components.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace BlazorApp4.Controllers
{
    [ApiController]
    [Route("webapi/[controller]")]
    public class PeriodicTableController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public PeriodicTableController(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;
        }

        // GET webapi/periodictable?source=json|db&page=1&pageSize=10
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string source = "json", [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;

            if (string.Equals(source, "db", StringComparison.OrdinalIgnoreCase))
            {
                var connStr = _config.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connStr))
                    return Problem(detail: "Database connection string 'DefaultConnection' is not configured.");

                var items = new List<Element>();
                int total = 0;

                using (var conn = new SqlConnection(connStr))
                {
                    await conn.OpenAsync();

                    // total count
                    using (var countCmd = new SqlCommand("SELECT COUNT(*) FROM Elements", conn))
                    {
                        var cnt = await countCmd.ExecuteScalarAsync();
                        total = (cnt != null) ? Convert.ToInt32(cnt) : 0;
                    }

                    // paging - offset fetch (SQL Server 2012+)
                    var offset = (page - 1) * pageSize;
                    var sql = "SELECT Number, Sign, Name, Position, Molar FROM Elements ORDER BY Number OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@offset", offset);
                        cmd.Parameters.AddWithValue("@pageSize", pageSize);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var el = new Element
                                {
                                    Number = reader.GetInt32(0),
                                    Sign = reader.IsDBNull(1) ? null : reader.GetString(1),
                                    Name = reader.IsDBNull(2) ? null : reader.GetString(2),
                                    Position = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                                    Molar = reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4)
                                };
                                items.Add(el);
                            }
                        }
                    }
                }

                return Ok(new { Items = items, Total = total, Page = page, PageSize = pageSize });
            }

            // default: source=json - try to read a local file periodictable.json in content root
            var filePath = Path.Combine(_env.ContentRootPath, "periodictable.json");
            List<Element> list = null!;
            if (System.IO.File.Exists(filePath))
            {
                var txt = await System.IO.File.ReadAllTextAsync(filePath);
                try
                {
                    list = JsonSerializer.Deserialize<List<Element>>(txt) ?? new List<Element>();
                }
                catch
                {
                    list = new List<Element>();
                }
            }

            if (list == null || !list.Any())
            {
                // fallback in-memory sample
                list = new List<Element>
                {
                    new Element { Number = 1, Sign = "H", Name = "Hydrogen", Position = 1, Molar = 1.008 },
                    new Element { Number = 2, Sign = "He", Name = "Helium", Position = 18, Molar = 4.0026 },
                    new Element { Number = 3, Sign = "Li", Name = "Lithium", Position = 1, Molar = 6.94 },
                    new Element { Number = 4, Sign = "Be", Name = "Beryllium", Position = 2, Molar = 9.0122 }
                };
            }

            var totalJson = list.Count;
            var itemsJson = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Ok(new { Items = itemsJson, Total = totalJson, Page = page, PageSize = pageSize });
        }

        // POST webapi/periodictable - accept JSON and echo back
        [HttpPost]
        public IActionResult Post([FromBody] Element element)
        {
            if (element == null) return BadRequest();
            // echo back the same object
            return Ok(element);
        }
    }
}
