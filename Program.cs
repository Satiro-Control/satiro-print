using System.Drawing.Printing;
using System.IO;
using PdfiumViewer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Collections.Generic;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

app.MapGet("/status", () => Results.Ok(new { status = "online" }));

app.MapGet("/impressoras", () =>
{
    var impressoras = PrinterSettings.InstalledPrinters.Cast<string>().ToList();
    return Results.Ok(impressoras);
});

app.MapPost("/imprimir", async (HttpContext context) =>
{
    try
    {
        string impressora = context.Request.Headers["X-Printer-Name"].ToString();
        
        if (string.IsNullOrEmpty(impressora))
            return Results.BadRequest("impressora nao informada");

        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        ms.Position = 0;

        using (var doc = PdfDocument.Load(ms))
        {
            var pd = new PrintDocument();
            pd.PrinterSettings.PrinterName = impressora;
            
            var pdfSize = doc.PageSizes[0];
            int w = (int)((pdfSize.Width / 72.0) * 100);
            int h = (int)((pdfSize.Height / 72.0) * 100);

            using (var printDoc = doc.CreatePrintDocument(PdfPrintMode.CutMargin))
            {
                printDoc.PrinterSettings = pd.PrinterSettings;
                
                printDoc.DefaultPageSettings.PaperSize = new PaperSize("Custom", w, h);
                printDoc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
                printDoc.DefaultPageSettings.Landscape = w > h; 
                
                printDoc.PrintController = new StandardPrintController(); 
                printDoc.Print();
            }
        }

        return Results.Ok();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.Run("http://localhost:8089");