using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ProtoDrawingCollector.csproj {
  public partial class Message : Form {
    public Message() {
      InitializeComponent();
    }

    //public delegate void AppendEvent(object o, EventArgs e);

    public void Append(string str) {
      rtbMessage.Text += str;
    }

    public void AppendLine(string str) {
      Append(str + "\n");
    }

    public void AppendLineEvent(object o, EventArgs e) {
      AppendLine(e.ToString());
    }

    public virtual void OnAppendEvent(EventArgs e) {
      BeginInvoke(new EventHandler(AppendLineEvent), this, e);
    }

    private void Message_Load(object sender, EventArgs e) {
      Location = Properties.Settings.Default.MessageLocation;
      Size = Properties.Settings.Default.MessageSize;
    }

    private void Message_FormClosing(object sender, FormClosingEventArgs e) {
      Properties.Settings.Default.MessageLocation = Location;
      Properties.Settings.Default.MessageSize = Size;
      Properties.Settings.Default.Save();
    }

    public EventHandler AppendEvent;

    private void Message_FormClosed(object sender, FormClosedEventArgs e) {
      System.GC.Collect(0, GCCollectionMode.Forced);
    }
  }
}