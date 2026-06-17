using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PdfiumViewer;
using Serilog;

namespace SatiroPrint
{
    static class Program
    {
        private static readonly Mutex _mutex = new Mutex(true, "SatiroPrint_UniqueInstance");
        private static readonly string _versao = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0";
        private static readonly string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SatiroPrint", "logs", "log-.txt");

        [STAThread]
        static void Main(string[] args)
        {
            if (!_mutex.WaitOne(TimeSpan.Zero, true))
            {
                MessageBox.Show("O Satiro Print já está em execução.", "Satiro Print", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Error()
                .WriteTo.File(_logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                .CreateLogger();

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (!PortaDisponivel(8089))
                {
                    MessageBox.Show("A porta 8089 já está em uso por outro processo.\nO Satiro Print não pode iniciar.", "Satiro Print", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                using var cts = new CancellationTokenSource();

                var apiTask = IniciarApiComRestart(args, cts.Token);

                using var notifyIcon = new NotifyIcon();
                notifyIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                notifyIcon.Text = "Satiro Print";
                notifyIcon.Visible = true;

                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Sair", null, (s, e) =>
                {
                    cts.Cancel();
                    Application.Exit();
                });
                notifyIcon.ContextMenuStrip = contextMenu;

                apiTask.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Log.Error(t.Exception, "API encerrada com erro critico");
                        notifyIcon.ShowBalloonTip(5000, "Satiro Print", "Erro interno. Reinicie o programa.", ToolTipIcon.Error);
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());

                Application.Run();
                notifyIcon.Visible = false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Erro critico na inicializacao");
            }
            finally
            {
                Log.CloseAndFlush();
                _mutex.ReleaseMutex();
            }
        }

        static bool PortaDisponivel(int porta)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, porta);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

        static async Task IniciarApiComRestart(string[] args, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await IniciarApi(args, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "API caiu, tentando reiniciar em 5 segundos");
                    await Task.Delay(5000, cancellationToken).ContinueWith(_ => { });
                }
            }
        }

        static async Task IniciarApi(string[] args, CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Logging.ClearProviders();

            builder.Services.AddCors(options =>
                options.AddDefaultPolicy(p =>
                    p.WithOrigins("http://localhost", "http://127.0.0.1")
                     .AllowAnyHeader()
                     .AllowAnyMethod()));

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = 50 * 1024 * 1024;
            });

            var app = builder.Build();

            app.UseCors();

            app.MapGet("/status", () => Results.Ok(new { status = "online", versao = _versao }));

            app.MapGet("/impressoras", () =>
            {
                var impressoras = PrinterSettings.InstalledPrinters.Cast<string>().ToList();
                return Results.Ok(impressoras);
            });

            app.MapPost("/imprimir", async (HttpContext context, CancellationToken ct) =>
            {
                try
                {
                    string impressora = context.Request.Headers["X-Printer-Name"].ToString().Trim();

                    if (string.IsNullOrEmpty(impressora))
                        return Results.BadRequest(new { erro = "impressora nao informada" });

                    var pd = new PrintDocument();
                    pd.PrinterSettings.PrinterName = impressora;

                    if (!pd.PrinterSettings.IsValid)
                        return Results.BadRequest(new { erro = "impressora invalida ou inacessivel" });

                    using var ms = new MemoryStream();
                    await context.Request.Body.CopyToAsync(ms, ct);

                    if (ms.Length == 0)
                        return Results.BadRequest(new { erro = "arquivo pdf vazio" });

                    ms.Position = 0;
                    var header = new byte[4];
                    await ms.ReadAsync(header, 0, 4, ct);
                    if (header[0] != '%' || header[1] != 'P' || header[2] != 'D' || header[3] != 'F')
                        return Results.BadRequest(new { erro = "arquivo invalido, esperado pdf" });

                    ms.Position = 0;

                    using var doc = PdfDocument.Load(ms);

                    if (doc.PageCount == 0)
                        return Results.BadRequest(new { erro = "pdf sem paginas" });

                    var pdfSize = doc.PageSizes[0];
                    int w = (int)((pdfSize.Width / 72.0) * 100);
                    int h = (int)((pdfSize.Height / 72.0) * 100);

                    using var printCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    printCts.CancelAfter(TimeSpan.FromSeconds(30));

                    await Task.Run(() =>
                    {
                        using var printDoc = doc.CreatePrintDocument(PdfPrintMode.CutMargin);
                        printDoc.PrinterSettings = pd.PrinterSettings;
                        printDoc.DefaultPageSettings.PaperSize = new PaperSize("Custom", w, h);
                        printDoc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
                        printDoc.DefaultPageSettings.Landscape = w > h;
                        printDoc.PrintController = new StandardPrintController();
                        printDoc.Print();
                    }, printCts.Token);

                    return Results.Ok(new { status = "impresso" });
                }
                catch (OperationCanceledException)
                {
                    return Results.StatusCode(499);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Erro ao imprimir");
                    return Results.StatusCode(500);
                }
            });

            await app.RunAsync("http://localhost:8089");
        }
    }
}