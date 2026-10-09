using System;
using System.Collections.Generic;

namespace QuickRoute.BusinessEntities.Importers
{
  /// <summary>
  /// A route importer. Importers never show user interface; when the user has to choose what to import,
  /// the choices are exposed through <see cref="GetImportChoices"/> and the caller sets <see cref="SelectedChoice"/>.
  /// </summary>
  public interface IRouteImporter
  {
    ImportResult ImportResult { get; set; }

    /// <summary>
    /// Gets the alternatives the user must choose between before importing, e.g. activities in a multi-activity file.
    /// An empty list means that no choice is needed.
    /// </summary>
    IList<string> GetImportChoices();

    /// <summary>
    /// The alternative to import, one of the values returned by <see cref="GetImportChoices"/>.
    /// </summary>
    string SelectedChoice { get; set; }

    void Import();
    event EventHandler<EventArgs> BeginWork;
    event EventHandler<EventArgs> EndWork;
    event EventHandler<WorkProgressEventArgs> WorkProgress;
  }
}
