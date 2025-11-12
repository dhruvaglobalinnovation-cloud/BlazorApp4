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
                    var sql = "SELECT Id, Sign, Name, Position, Molar FROM Elements ORDER BY Id OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
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
                                    Id = reader.GetInt32(0),
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
                    new Element { Id = 1, Sign = "H", Name = "Hydrogen", Position = 1, Molar = 1.008 },
                    new Element { Id = 2, Sign = "He", Name = "Helium", Position = 18, Molar = 4.0026 },
                    new Element { Id = 3, Sign = "Li", Name = "Lithium", Position = 1, Molar = 6.94 },
                    new Element { Id = 4, Sign = "Be", Name = "Beryllium", Position = 2, Molar = 9.0122 }
                };
            }

            var totalJson = list.Count;
            var itemsJson = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Ok(new { Items = itemsJson, Total = totalJson, Page = page, PageSize = pageSize });
        }

        // POST webapi/periodictable - create new Element in DB (if configured) or JSON file
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] Element element)
        {
            if (element == null) return BadRequest();

            var connStr = _config.GetConnectionString("DefaultConnection");
            var filePath = Path.Combine(_env.ContentRootPath, "periodictable.json");

            if (!string.IsNullOrWhiteSpace(connStr))
            {
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                // Determine new Id if not provided
                if (element.Id == 0)
                {
                    using var maxCmd = new SqlCommand("SELECT ISNULL(MAX(Id), 0) FROM Elements", conn);
                    var obj = await maxCmd.ExecuteScalarAsync();
                    element.Id = (obj != null) ? Convert.ToInt32(obj) + 1 : 1;
                }

                // Insert into DB
                using (var insertCmd = new SqlCommand("INSERT INTO Elements(Id, Sign, Name, Position, Molar) VALUES(@Id, @Sign, @Name, @Position, @Molar)", conn))
                {
                    insertCmd.Parameters.AddWithValue("@Id", element.Id);
                    insertCmd.Parameters.AddWithValue("@Sign", (object?)element.Sign ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@Name", (object?)element.Name ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@Position", element.Position);
                    insertCmd.Parameters.AddWithValue("@Molar", element.Molar);
                    await insertCmd.ExecuteNonQueryAsync();
                }

                // Dump DB to JSON
                var items = new List<Element>();
                using (var cmd = new SqlCommand("SELECT Id, Sign, Name, Position, Molar FROM Elements ORDER BY Id", conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        items.Add(new Element
                        {
                            Id = reader.GetInt32(0),
                            Sign = reader.IsDBNull(1) ? null : reader.GetString(1),
                            Name = reader.IsDBNull(2) ? null : reader.GetString(2),
                            Position = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                            Molar = reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4)
                        });
                    }
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var outTxt = JsonSerializer.Serialize(items, options);
                await System.IO.File.WriteAllTextAsync(filePath, outTxt);

                return CreatedAtAction(nameof(Get), new { source = "db", page = 1, pageSize = 10 }, element);
            }

            // JSON-only scenario: append to file
            List<Element> list;
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
            else
            {
                list = new List<Element>();
            }

            if (element.Id == 0)
            {
                var max = list.Any() ? list.Max(x => x.Id) : 0;
                element.Id = max + 1;
            }

            list.Add(element);

            var opts = new JsonSerializerOptions { WriteIndented = true };
            await System.IO.File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(list, opts));

            return CreatedAtAction(nameof(Get), new { source = "json", page = 1, pageSize = 10 }, element);
        }

        // PUT webapi/periodictable - accept JSON Element and update DB (if configured) and periodictable.json
        [HttpPut]
        public async Task<IActionResult> Put([FromBody] Element element)
        {
            if (element == null) return BadRequest();

            var connStr = _config.GetConnectionString("DefaultConnection");
            var filePath = Path.Combine(_env.ContentRootPath, "periodictable.json");

            if (!string.IsNullOrWhiteSpace(connStr))
            {
                // Update DB first (upsert)
                using (var conn = new SqlConnection(connStr))
                {
                    await conn.OpenAsync();

                    // If Id == 0, assign new Id
                    if (element.Id == 0)
                    {
                        using var maxCmd = new SqlCommand("SELECT ISNULL(MAX(Id), 0) FROM Elements", conn);
                        var obj = await maxCmd.ExecuteScalarAsync();
                        var max = (obj != null) ? Convert.ToInt32(obj) : 0;
                        element.Id = max + 1;
                    }

                    // Check if exists
                    using (var existsCmd = new SqlCommand("SELECT COUNT(*) FROM Elements WHERE Id = @Id", conn))
                    {
                        existsCmd.Parameters.AddWithValue("@Id", element.Id);
                        var cnt = (int)await existsCmd.ExecuteScalarAsync();
                        if (cnt > 0)
                        {
                            // update
                            using var updateCmd = new SqlCommand("UPDATE Elements SET Sign=@Sign, Name=@Name, Position=@Position, Molar=@Molar WHERE Id=@Id", conn);
                            updateCmd.Parameters.AddWithValue("@Sign", (object?)element.Sign ?? DBNull.Value);
                            updateCmd.Parameters.AddWithValue("@Name", (object?)element.Name ?? DBNull.Value);
                            updateCmd.Parameters.AddWithValue("@Position", element.Position);
                            updateCmd.Parameters.AddWithValue("@Molar", element.Molar);
                            updateCmd.Parameters.AddWithValue("@Id", element.Id);
                            await updateCmd.ExecuteNonQueryAsync();
                        }
                        else
                        {
                            // insert
                            using var insertCmd = new SqlCommand("INSERT INTO Elements(Id, Sign, Name, Position, Molar) VALUES(@Id, @Sign, @Name, @Position, @Molar)", conn);
                            insertCmd.Parameters.AddWithValue("@Id", element.Id);
                            insertCmd.Parameters.AddWithValue("@Sign", (object?)element.Sign ?? DBNull.Value);
                            insertCmd.Parameters.AddWithValue("@Name", (object?)element.Name ?? DBNull.Value);
                            insertCmd.Parameters.AddWithValue("@Position", element.Position);
                            insertCmd.Parameters.AddWithValue("@Molar", element.Molar);
                            await insertCmd.ExecuteNonQueryAsync();
                        }
                    }

                    // After DB upsert, dump table to JSON file
                    var items = new List<Element>();
                    using (var cmd = new SqlCommand("SELECT Id, Sign, Name, Position, Molar FROM Elements ORDER BY Id", conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            items.Add(new Element
                            {
                                Id = reader.GetInt32(0),
                                Sign = reader.IsDBNull(1) ? null : reader.GetString(1),
                                Name = reader.IsDBNull(2) ? null : reader.GetString(2),
                                Position = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                                Molar = reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4)
                            });
                        }
                    }

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var outTxt = JsonSerializer.Serialize(items, options);
                    await System.IO.File.WriteAllTextAsync(filePath, outTxt);

                    return Ok(element);
                }
            }

            // No DB configured - update JSON file only (existing behavior)
            List<Element> list;
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
            else
            {
                list = new List<Element>();
            }

            if (element.Id == 0)
            {
                var max = list.Any() ? list.Max(x => x.Id) : 0;
                element.Id = max + 1;
                list.Insert(0, element);
            }
            else
            {
                var idx = list.FindIndex(x => x.Id == element.Id);
                if (idx >= 0)
                    list[idx] = element;
                else
                    list.Add(element);
            }

            var optionsJson = new JsonSerializerOptions { WriteIndented = true };
            var outText = JsonSerializer.Serialize(list, optionsJson);
            await System.IO.File.WriteAllTextAsync(filePath, outText);

            return Ok(element);
        }

        // POST webapi/periodictable/saveall - accept full list and overwrite periodictable.json
        [HttpPost("saveall")]
        public async Task<IActionResult> SaveAll([FromBody] List<Element> elements)
        {
            if (elements == null) return BadRequest();
            var filePath = Path.Combine(_env.ContentRootPath, "periodictable.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            var outTxt = JsonSerializer.Serialize(elements, options);
            await System.IO.File.WriteAllTextAsync(filePath, outTxt);
            return Ok(new { Saved = elements.Count });
        }
    }
}
