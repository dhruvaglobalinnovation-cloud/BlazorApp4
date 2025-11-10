using System;
using System.Collections.Generic;
using System.Linq;
using BlazorApp4.Components.Models;

namespace BlazorApp4.Components.Data
{
    public static class SampleData
    {
        public static List<WeatherForecast> GetForecasts(int count = 25)
        {
            var startDate = DateOnly.FromDateTime(DateTime.Now);
            var summaries = new[] { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching" };
            var rnd = new Random();

            return Enumerable.Range(1, count).Select(i => new WeatherForecast
            {
                Id = i,
                Date = startDate.AddDays(i),
                TemperatureC = rnd.Next(-20, 55),
                Summary = summaries[rnd.Next(summaries.Length)]
            }).ToList();
        }
    }
}
