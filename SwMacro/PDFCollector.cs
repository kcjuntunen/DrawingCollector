using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.Runtime.InteropServices;

using BuildPDF.csproj;

public class PDFCollector {
  private List<FileInfo> lfi = new List<FileInfo>();
  private List<KeyValuePair<string, string>> nf = new List<KeyValuePair<string, string>>();
  private string targetExt = string.Empty;

  public PDFCollector(SldWorks swApp, System.Collections.Specialized.StringCollection hashes, DrawingData dd) {
    _swApp = swApp;
    _hashes = hashes;
    _d = dd;
  }

  public static event EventHandler file_added;
  public static event EventHandler done;
  public delegate void AppendEvent(object o, EventArgs e);

  public enum CreateFileType {
    PDFS,
    DXFS
  }

  public static void OnAppend(EventArgs e) {
    EventHandler handler = file_added;
    if (handler != null) {
      handler(new object(), e);
    }
  }

  public static void OnDone(EventArgs e) {
    EventHandler handler = done;
    if (handler != null) {
      handler(new object(), e);
    }
  }

  public void Collect() {
    string fullpath = (SwApp.ActiveDoc as ModelDoc2).GetPathName();
    SWTableType swt = null;
    ModelDoc2 md = (ModelDoc2)SwApp.ActiveDoc;
    try {
      swt = new SWTableType(md, Hashes);
    } catch (SWTableTypeException te) {
      OnAppend(new AppendEventArgs(string.Format("{0} {1}", te.Message, md.GetTitle())));
      System.Diagnostics.Debug.WriteLine(te.Message);
    } catch (Exception e) {
      OnAppend(new AppendEventArgs(e.Message));
      System.Diagnostics.Debug.WriteLine(e.Message);
    }
    FileInfo fi = new FileInfo(fullpath);
    create_dwg(fi);
    OnAppend(new AppendEventArgs(string.Format("Added {0}...", fi.Name)));
    FileInfo top_level = d.GetPath(Path.GetFileNameWithoutExtension(fullpath));
    lfi.Add(top_level);
    OnAppend(new AppendEventArgs(string.Format(@"Using {0}...", swt.found_bom.Name)));
    collect_drwgs(md, swt, 1);
    OnDone(EventArgs.Empty);
  }

  private void collect_drwgs(ModelDoc2 md, SWTableType swt, int lvl) {
    string title = md.GetTitle();

    List<FileInfo> ss = new List<FileInfo>();
    if (swt != null) {
      string part = string.Empty;
      bool in_lfi;
      bool in_nf;
      for (int i = 1; i < swt.RowCount; i++) {
        string ap_arg_ = string.Format(@"item: {0} ({1})", part, swt.GetProperty(i, @"DESCRIPTION"));
        AppendEventArgs a_ = new AppendEventArgs(ap_arg_);
        OnAppend(a_);
        part = swt.GetProperty(i, swt.PartColumn);
        if (!part.StartsWith("0")) {
          FileInfo partpath = swt.get_path2(part);
          string t = string.Empty;
          try {
            t = partpath.FullName.ToUpper();
          } catch (NullReferenceException) {
            // it's OK
          }
          string ext = Path.GetExtension(t).ToUpper();
          if (t.Length < 1) continue;
          FileInfo dwg = new FileInfo(t.Replace(ext, ".SLDDRW"));
          FileInfo fi = new FileInfo(t.Replace(ext, targetExt));
          if (!dwg.Exists) {
            string filename = Path.GetFileNameWithoutExtension(t);
            FileInfo maybe_md_= new FileInfo(t.Replace(filename, part));
            if (maybe_md_.Exists) {
              dwg = new FileInfo(maybe_md_.FullName.Replace(ext, ".SLDDRW"));
            }
          }
          if (dwg.Exists && !fi.Exists) {
            create_dwg(dwg);
            fi = new FileInfo(fi.FullName);
            SwApp.CloseDoc(dwg.FullName);
          }
          in_lfi = is_in(part, lfi);
          in_nf = is_in(part, nf);
          if (dwg != null) {
            if (!in_lfi && fi.Exists) {
              ss.Add(fi);
              OnAppend(new AppendEventArgs(string.Format("Added {0}", fi.Name)));
            } else {
              continue;
            }
          } else {
            if (!in_nf) {
              nf.Add(new KeyValuePair<string, string>(part, title));
              OnAppend(new AppendEventArgs(string.Format("{0} NOT found", part)));
            } else {
              continue;
            }
          }
        } else {
          System.Diagnostics.Debug.WriteLine("Skipping " + part);
        }
      }

      lfi.AddRange(ss);
    }
    string indent = string.Empty;
    if (Recurse) {
      foreach (FileInfo f in ss) {
        for (int i = 0; i < lvl; i++) {
          indent = indent + " > ";
        }
        if (f != null) {
          string doc = f.FullName.ToUpper().Replace(targetExt, ".SLDDRW");
          OnAppend(new AppendEventArgs(string.Format("{0} Opening '{1}'...", indent, doc)));
          SwApp.OpenDoc(doc, (int)swDocumentTypes_e.swDocDRAWING);
          SwApp.ActivateDoc(doc);
          ModelDoc2 m = (ModelDoc2)SwApp.ActiveDoc;
          SWTableType innerswt = null;
          try {
            innerswt = new SWTableType(m, Hashes);
            OnAppend(new AppendEventArgs(string.Format("{0} Found table: {1}", indent, innerswt.found_bom.Name)));
          } catch (Exception e) {
            System.Diagnostics.Debug.WriteLine(e.Message);
          }
          System.Diagnostics.Debug.WriteLine("ss   : " + f.Name);
          System.Diagnostics.Debug.WriteLine(doc);
          collect_drwgs(m, innerswt, ++lvl);
          OnAppend(new AppendEventArgs(string.Format("{0} Closing '{1}'...", indent, doc)));
          indent = string.Empty;
          --lvl;
          SwApp.CloseDoc(doc);
        }
      }
    }
  }

  private string find_part_column(SWTableType swt) {
    foreach (string s in swt.Columns) {
      if (s.ToUpper().Contains("PART")) {
        return s;
      }
    }
    return "PART NUMBER";
  }

  private void create_dwg(FileInfo p) {
    int dt = (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocDRAWING;
    int odo = (int)SolidWorks.Interop.swconst.swOpenDocOptions_e.swOpenDocOptions_Silent;
    int err = 0;
    int warn = 0;
    string newName = p.Name.Replace(".SLDDRW", targetExt);
    string tmpFile = string.Format(@"{0}\{1}", Path.GetTempPath(), newName);
    string fileName = p.FullName.Replace(".SLDDRW", targetExt);
    int saveVersion = (int)swSaveAsVersion_e.swSaveAsCurrentVersion;
    int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent;
    bool success;
    OnAppend(new AppendEventArgs(string.Format("Creating {0}...",
      p.Name.Replace(@".SLDDRW", targetExt))));
    SwApp.OpenDocSilent(p.FullName, dt, ref odo);
    SwApp.ActivateDoc3(p.FullName,
      true,
      (int)SolidWorks.Interop.swconst.swRebuildOnActivation_e.swDontRebuildActiveDoc, ref err);
    success = (SwApp.ActiveDoc as ModelDoc2).SaveAs4(tmpFile, saveVersion, saveOptions, ref err, ref warn);
    try {
      File.Copy(tmpFile, fileName, true);
    } catch (UnauthorizedAccessException uae) {
      throw new Exceptions.BuildPDFException(
          String.Format("You don't have the reqired permission to access '{0}'.", fileName),
          uae);
    } catch (ArgumentException ae) {
      throw new Exceptions.BuildPDFException(
          String.Format("Either '{0}' or '{1}' is not a proper file name.", tmpFile, fileName),
          ae);
    } catch (PathTooLongException ptle) {
      throw new Exceptions.BuildPDFException(
          String.Format("Source='{0}'; Dest='{1}' <= One of these is too long.", tmpFile, fileName),
          ptle);
    } catch (DirectoryNotFoundException dnfe) {
      throw new Exceptions.BuildPDFException(
          String.Format("Source='{0}'; Dest='{1}' <= One of these is invalid.", tmpFile, fileName),
          dnfe);
    } catch (FileNotFoundException fnfe) {
      throw new Exceptions.BuildPDFException(
          String.Format("Crap! I lost '{0}'!", tmpFile),
          fnfe);
    } catch (IOException) {
      System.Windows.Forms.MessageBox.Show(
          String.Format("If you have the file, '{0}', selected in an Explorer window, " +
          "you may have to close it.", fileName), "This file is open somewhere.",
          System.Windows.Forms.MessageBoxButtons.OK,
          System.Windows.Forms.MessageBoxIcon.Error);
    } catch (NotSupportedException nse) {
      throw new Exceptions.BuildPDFException(
          String.Format("Source='{0}'; Dest='{1}' <= One of these is an invalid format.",
          tmpFile, fileName), nse);
    }
  }

  //private void create_pdf(FileInfo p) {
  //  OnAppend(new AppendEventArgs(string.Format("Creating {0}...",
  //    p.Name.Replace(@".SLDDRW", ProtoDrawingCollector.csproj.Properties.Settings.Default.Suffix + @".PDF"))));
  //  int dt = (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocDRAWING;
  //  int odo = (int)SolidWorks.Interop.swconst.swOpenDocOptions_e.swOpenDocOptions_Silent;
  //  int err = 0;
  //  int warn = 0;
  //  SwApp.OpenDocSilent(p.FullName, dt, ref odo);
  //  SwApp.ActivateDoc3(p.FullName,
  //    true,
  //    (int)SolidWorks.Interop.swconst.swRebuildOnActivation_e.swDontRebuildActiveDoc, ref err);
  //  string newName = p.Name.Replace(".SLDDRW", ".PDF");
  //  string tmpFile = string.Format(@"{0}\{1}", Path.GetTempPath(), newName);
  //  string fileName = p.FullName.Replace(".SLDDRW", ".PDF");
  //  int saveVersion = (int)swSaveAsVersion_e.swSaveAsCurrentVersion;
  //  int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent;
  //  bool success;
  //  success = (SwApp.ActiveDoc as ModelDoc2).SaveAs4(tmpFile, saveVersion, saveOptions, ref err, ref warn);
  //  try {
  //    File.Copy(tmpFile, fileName, true);
  //  } catch (UnauthorizedAccessException uae) {
  //    throw new Exceptions.BuildPDFException(
  //        String.Format("You don't have the reqired permission to access '{0}'.", fileName),
  //        uae);
  //  } catch (ArgumentException ae) {
  //    throw new Exceptions.BuildPDFException(
  //        String.Format("Either '{0}' or '{1}' is not a proper file name.", tmpFile, fileName),
  //        ae);
  //  } catch (PathTooLongException ptle) {
  //    throw new Exceptions.BuildPDFException(
  //        String.Format("Source='{0}'; Dest='{1}' <= One of these is too long.", tmpFile, fileName),
  //        ptle);
  //  } catch (DirectoryNotFoundException dnfe) {
  //    throw new Exceptions.BuildPDFException(
  //        String.Format("Source='{0}'; Dest='{1}' <= One of these is invalid.", tmpFile, fileName),
  //        dnfe);
  //  } catch (FileNotFoundException fnfe) {
  //    throw new Exceptions.BuildPDFException(
  //        String.Format("Crap! I lost '{0}'!", tmpFile),
  //        fnfe);
  //  } catch (IOException) {
  //    System.Windows.Forms.MessageBox.Show(
  //        String.Format("If you have the file, '{0}', selected in an Explorer window, " +
  //        "you may have to close it.", fileName), "This file is open somewhere.",
  //        System.Windows.Forms.MessageBoxButtons.OK,
  //        System.Windows.Forms.MessageBoxIcon.Error);
  //  } catch (NotSupportedException nse) {
  //    throw new Exceptions.BuildPDFException(
  //        String.Format("Source='{0}'; Dest='{1}' <= One of these is an invalid format.",
  //        tmpFile, fileName), nse);
  //  }
  //}

  //private void create_dxf(FileInfo p) {
  //  OnAppend(new AppendEventArgs(string.Format("Creating {0}...",
  //    p.Name.Replace(@".SLDDRW", ProtoDrawingCollector.csproj.Properties.Settings.Default.Suffix + @".DXF"))));
  //  int dt = (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocDRAWING;
  //  int odo = (int)SolidWorks.Interop.swconst.swOpenDocOptions_e.swOpenDocOptions_Silent;
  //  int err = 0;
  //  int warn = 0;
  //  SwApp.OpenDocSilent(p.FullName, dt, ref odo);
  //  SwApp.ActivateDoc3(p.FullName,
  //    true,
  //    (int)SolidWorks.Interop.swconst.swRebuildOnActivation_e.swDontRebuildActiveDoc, ref err);
  //  string newName = p.Name.Replace(".SLDDRW", ".DXF");
  //  string tmpFile = string.Format(@"{0}\{1}", Path.GetTempPath(), newName);
  //  string fileName = p.FullName.Replace(".SLDDRW", ".DXF");
  //  int saveVersion = (int)swSaveAsVersion_e.swSaveAsCurrentVersion;
  //  int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent;
  //  bool success;
  //  success = (SwApp.ActiveDoc as ModelDoc2).SaveAs4(tmpFile, saveVersion, saveOptions, ref err, ref warn);
  //  try {
  //    File.Copy(tmpFile, fileName, true);
  //  } catch (UnauthorizedAccessException uae) {
  //    throw new Exceptions.BuildPDFException(
  //        String.Format("You don't have the reqired permission to access '{0}'.", fileName),
  //        uae);
  //  } catch (ArgumentException ae) {
  //    throw new Exceptions.BuildPDFException(
  //        String.Format("Either '{0}' or '{1}' is not a proper file name.", tmpFile, fileName),
  //        ae);
  //  } catch (PathTooLongException ptle) {
  //    throw new Exceptions.BuildPDFException(
  //        String.Format("Source='{0}'; Dest='{1}' <= One of these is too long.", tmpFile, fileName),
  //        ptle);
  //  } catch (DirectoryNotFoundException dnfe) {
  //    throw new Exceptions.BuildPDFException(
  //        String.Format("Source='{0}'; Dest='{1}' <= One of these is invalid.", tmpFile, fileName),
  //        dnfe);
  //  } catch (FileNotFoundException fnfe) {
  //    throw new Exceptions.BuildPDFException(
  //        String.Format("Crap! I lost '{0}'!", tmpFile),
  //        fnfe);
  //  } catch (IOException) {
  //    System.Windows.Forms.MessageBox.Show(
  //        String.Format("If you have the file, '{0}', selected in an Explorer window, " +
  //        "you may have to close it.", fileName), "This file is open somewhere.",
  //        System.Windows.Forms.MessageBoxButtons.OK,
  //        System.Windows.Forms.MessageBoxIcon.Error);
  //  } catch (NotSupportedException nse) {
  //    throw new Exceptions.BuildPDFException(
  //        String.Format("Source='{0}'; Dest='{1}' <= One of these is an invalid format.",
  //        tmpFile, fileName), nse);
  //  }
  //}

  public static bool is_in(FileInfo f, List<FileInfo> l) {
    foreach (FileInfo fi in l) {
      if (f != null && Path.GetFileNameWithoutExtension(f.Name).ToUpper() ==
        Path.GetFileNameWithoutExtension(fi.Name).ToUpper()) {
        return true;
      }
    }
    return false;
  }

  public static bool is_in(FileInfo f, List<KeyValuePair<string, string>> l) {
    foreach (KeyValuePair<string, string> fi in l) {
      if (f != null && Path.GetFileNameWithoutExtension(f.Name).ToUpper() ==
  Path.GetFileNameWithoutExtension(fi.Key).ToUpper()) {
        return true;
      }
    }
    return false;
  }

  public static bool is_in(string f, List<FileInfo> l) {
    foreach (FileInfo fi in l) {
      try {
        if (f != null && f.ToUpper() == Path.GetFileNameWithoutExtension(fi.Name).ToUpper()) {
          return true;
        }
      } catch (NullReferenceException n) {
        OnAppend(new AppendEventArgs(string.Format("{0}: {1} != null", n.Message, f)));
        return false;
      }
    }
    return false;
  }

  public static bool is_in(string f, List<KeyValuePair<string, string>> l) {
    foreach (KeyValuePair<string, string> fi in l) {
      if (f != null && f.ToUpper() == Path.GetFileNameWithoutExtension(fi.Key).ToUpper()) {
        return true;
      }
    }
    return false;
  }

  public List<FileInfo> PDFCollection {
    get { return lfi; }
    set { lfi = value; }
  }

  public List<KeyValuePair<string, string>> NotFound {
    get { return nf; }
    set { nf = value; }
  }

  private SldWorks _swApp;

  public SldWorks SwApp {
    get { return _swApp; }
    private set { _swApp = value; }
  }

  private System.Collections.Specialized.StringCollection _hashes;

  public System.Collections.Specialized.StringCollection Hashes {
    get { return _hashes; }
    private set { _hashes = value; }
  }

  private DrawingData _d;

  public DrawingData d {
    get { return _d; }
    private set { _d = value; }
  }

  private bool _recurse;

  public bool Recurse {
    get { return _recurse; }
    set { _recurse = value; }
  }

  private bool _deletePDFs;

  public bool DeletePDFs {
    get { return _deletePDFs; }
    set { _deletePDFs = value; }
  }

  private CreateFileType _typeToCreate;

  public CreateFileType TypeToCreate {
    get { return _typeToCreate; }
    set {
      _typeToCreate = value;

      switch (TypeToCreate) {
        case CreateFileType.PDFS:
          targetExt = @".PDF";
          break;
        case CreateFileType.DXFS:
          targetExt = @".DXF";
          break;
        default:
          break;
      }

    }
  }
      


  class AppendEventArgs : EventArgs {
    public AppendEventArgs() {
      _msg = string.Empty;
    }


    public AppendEventArgs(string msg) {
      _msg = msg;
    }

    public override string ToString() {
      return _msg;
    }

    private string _msg;

    public string Message {
      get { return _msg; }
      set { _msg = value; }
    }

  }

}
