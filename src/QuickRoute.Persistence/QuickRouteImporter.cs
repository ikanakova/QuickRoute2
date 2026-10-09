using System;
using System.Collections.Generic;
using QuickRoute.BusinessEntities;
using QuickRoute.BusinessEntities.Importers;

namespace QuickRoute.Persistence
{
  /// <summary>
  /// Imports the route and laps of the first session of a QuickRoute document (.qrtx, .qrt or QuickRoute JPEG).
  /// </summary>
  public class QuickRouteImporter : IRouteFileImporter
  {
    private readonly DocumentFileService documentFileService;

    public QuickRouteImporter(DocumentFileService documentFileService)
    {
      this.documentFileService = documentFileService ?? throw new ArgumentNullException(nameof(documentFileService));
    }

    public ImportResult ImportResult { get; set; }

    public string FileName { get; set; }

    public IList<string> GetImportChoices()
    {
      return Array.Empty<string>();
    }

    public string SelectedChoice { get; set; }

    public void Import()
    {
      ImportResult = new ImportResult();
      BeginWork?.Invoke(this, EventArgs.Empty);
      try
      {
        var document = documentFileService.Open(FileName);
        if (document != null && document.Sessions.Count > 0)
        {
          ImportResult.Succeeded = true;
          ImportResult.Route = document.Sessions[0].Route;
          ImportResult.Laps = document.Sessions[0].Laps;
        }
        else
        {
          ImportResult.Succeeded = false;
        }
      }
      finally
      {
        EndWork?.Invoke(this, EventArgs.Empty);
      }
    }

    public event EventHandler<EventArgs> BeginWork;

    public event EventHandler<EventArgs> EndWork;

#pragma warning disable CS0067 // part of the importer contract; this importer reports no intermediate progress
    public event EventHandler<WorkProgressEventArgs> WorkProgress;
#pragma warning restore CS0067
  }
}
