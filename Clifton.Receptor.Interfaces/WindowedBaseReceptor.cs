﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using Clifton.MycroParser;

namespace Clifton.Receptor.Interfaces
{
	public abstract class WindowedBaseReceptor : BaseReceptor
	{
		[UserConfigurableProperty("WindowName")]
		public string WindowName { get; set; }

		[UserConfigurableProperty("X")]
		public int WindowX { get; set; }

		[UserConfigurableProperty("Y")]
		public int WindowY { get; set; }

		[UserConfigurableProperty("W")]
		public int WindowWidth { get; set; }

		[UserConfigurableProperty("H")]
		public int WindowHeight { get; set; }

		protected Clifton.MycroParser.MycroParser mycroParser;
		protected Form form;
		protected string displayFormFilename;
		protected bool showOnStartup;

		public WindowedBaseReceptor(string displayFormFilename, bool showOnStartup, IReceptorSystem rsys)
			: base(rsys)
		{
			this.displayFormFilename = displayFormFilename;
			this.showOnStartup = showOnStartup;
		}

		public override void Initialize()
		{
			base.Initialize();

			if (showOnStartup)
			{
				InitializeUI();
			}
		}

		public override void EndSystemInit()
		{
			base.EndSystemInit();

			if (showOnStartup)
			{
				UpdateFormLocationAndSize();
				UpdateCaption();
			}
		}

		public override void Terminate()
		{
			base.Terminate();

			if (form != null)
			{
				form.Close();
				form = null;
			}
		}

		protected virtual void InitializeUI()
		{
			mycroParser = new Clifton.MycroParser.MycroParser();
			mycroParser.AddInstance("form", this);
			form = mycroParser.Load<Form>(displayFormFilename, this);
			form.Show();

			// Wire up the location changed event after the form has initialized,
			// so we don't generate this event during form creation.  That way,
			// the user's config will be preserved and used when the system
			// finishes initialization.
			form.LocationChanged += OnLocationChanged;
			form.SizeChanged += OnSizeChanged;
			form.FormClosing += OnFormClosing;
		}

		protected void OnLocationChanged(object sender, EventArgs e)
		{
			WindowX = form.Location.X;
			WindowY = form.Location.Y;
		}

		protected void OnSizeChanged(object sender, EventArgs e)
		{
			WindowWidth = form.Size.Width;
			WindowHeight = form.Size.Height;
		}

		protected virtual void OnFormClosing(object sender, FormClosingEventArgs e)
		{
			form = null;
		}

		protected virtual void UpdateFormLocationAndSize()
		{
			if (form != null)
			{
				Point loc = new Point(WindowX, WindowY);

				// Only update if user has changed the size from its declarative value.
				if (WindowX != 0 && WindowY != 0)
				{
					// If the starting coordinate is offscreen on this computer, then put it at (0,0)
					if (!SystemInformation.VirtualScreen.Contains(loc))
					{
						loc = new Point(0, 0);
					}

					form.Location = loc;
				}

				// Only update if user has changed the size from its declarative value.
				if (WindowWidth != 0 && WindowHeight != 0)
				{
					Size sz = new Size(WindowWidth, WindowHeight);
					Size diff = SystemInformation.VirtualScreen.Size - sz;

					// If the the width doesn't fit in the specified dimensions, then use 1/4 of the screen.
					if ( (diff.Width < 0) || (diff.Height < 0) )
					{
						sz = new Size(SystemInformation.VirtualScreen.Width / 4, SystemInformation.VirtualScreen.Height / 4);
					}

					form.Size = sz;
				}
			}
		}

		protected virtual void UpdateCaption()
		{
			if (form != null)
			{
				if (!String.IsNullOrEmpty(WindowName) && (form != null))
				{
					form.Text = WindowName;
				}
			}
		}

		protected virtual void ReinitializeUI()
		{
			InitializeUI();
			UpdateFormLocationAndSize();
			UpdateCaption();
		}
	}
}
