﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using System.Drawing;
using System.Threading;
using UV_DLP_3D_Printer.Slicing;

namespace UV_DLP_3D_Printer
{
    /*
     * This class controls print jobs from start to finish. It feeds the generated sliced images
     * one at a time, along with control information and GCode over the PrinterInterface
     * it can now also control FDM builds
     * 
     */
    /*
     This class raises an event when the printing starts,
     * when it stops
     * when it's cancelled 
     * and each time the layer changes
     */
    public enum eBuildStatus
    {
        eBuildStarted,
        eBuildCancelled,
        eBuildPaused,
        eBuildResumed,
        eLayerCompleted,
        eBuildCompleted,
        eBuildStatusUpdate
    }
    

    public delegate void delBuildStatus(eBuildStatus printstat,string message);
    public delegate void delPrinterLayer(Bitmap bmplayer, int layernum, int layertype); // this is raised to display the next layer, mainly for UV DLP

    public class BuildManager
    {
        private  const int STATE_START                = 0;
        private  const int STATE_DO_NEXT_LAYER        = 1;
        private  const int STATE_WAITING_FOR_LAYER    = 2;
        private  const int STATE_CANCELLED            = 3;
        private  const int STATE_IDLE                 = 4;
        private  const int STATE_DONE                 = 5;

        public const int SLICE_NORMAL                  =  0;
        public const int SLICE_BLANK                   = -1;
        public const int SLICE_CALIBRATION             = -2;

        

        public delBuildStatus BuildStatus; // the delegate to let the rest of the world know
        public delPrinterLayer PrintLayer; // the delegate to show a new layer (UV DLP Printers)
        private bool m_printing = false;
        private bool m_paused = false;
        private int m_curlayer = 0; // the current visible slice layer index #
        SliceFile m_sf = null; // current file we're building
        GCodeFile m_gcode = null; // a reference from the active gcode file
        int m_gcodeline = 0; // which line of GCode are we currently on.
        int m_state = STATE_IDLE; // the state machine variable
        private Thread m_runthread; // a thread to run all this..
        private bool m_running; // a var to control thread life
        private DateTime m_printstarttime;
        private System.Timers.Timer m_buildtimer;
        private const int BUILD_TIMER_INTERVAL = 1000; // 1 second updates
        Bitmap m_blankimage = null; // a blank image to display
        Bitmap m_calibimage = null; // a calibration image to display
        private DateTime m_buildstarttime;
        private string estimatedbuildtime = "";
        public BuildManager() 
        {
            m_buildtimer = new System.Timers.Timer();
            m_buildtimer.Elapsed += new ElapsedEventHandler(m_buildtimer_Elapsed);
            m_buildtimer.Interval = BUILD_TIMER_INTERVAL;
        }
        private void StartBuildTimer() 
        {
            m_buildtimer.Start();
        }
        private void StopBuildTimer() 
        {
            m_buildtimer.Stop();
        }
        void m_buildtimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                double percentdone = 0.0;
                if (m_gcode != null)
                {
                    double totallines = m_gcode.Lines.Length;
                    double curline = m_gcodeline;
                    percentdone = (curline / totallines) * 100.0;
                    TimeSpan span = DateTime.Now.Subtract(m_buildstarttime);

                    string tm = //"Elapsed " + span.Hours + ":" + span.Minutes + ":" + span.Seconds + " of " + EstimateBuildTime(m_gcode);
                    tm = String.Format("{0:00}:{1:00}:{2:00}", span.Hours, span.Minutes, span.Seconds);
                    tm = "Elapsed " + tm;
                    tm += " of " + estimatedbuildtime;
                    string mess = tm +  " - " + string.Format("{0:0.00}",percentdone) + "% Completed";
                    RaiseStatusEvent(eBuildStatus.eBuildStatusUpdate,mess);
                }
            }
            catch (Exception ex) 
            {
                DebugLogger.Instance().LogError(ex.Message);
            }
        }
        /// <summary>
        /// This function will return the estimated build time for UV DLP print Jobs
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static String EstimateBuildTime(GCodeFile file) 
        {
            int bt = 0; // in milliseconds
            bool done = false;
            int gidx = 0;
            while (!done) 
            {
                if (gidx >= file.Lines.Length)
                {
                    done = true;
                    break;
                }

                String line = file.Lines[gidx++];
                if (line.Length > 0)
                {
                    // if the line is a comment, parse it to see if we need to take action
                    if (line.Contains("<Delay> "))// get the delay
                    {
                        int delay = getvarfromline(line);
                        bt += delay;
                    }
                }
            }
            TimeSpan ts = new TimeSpan();
            ts = TimeSpan.FromMilliseconds(bt);
            return String.Format("{0:00}:{1:00}:{2:00}", ts.Hours, ts.Minutes, ts.Seconds);
        }
        public void ShowCalibration(int xres, int yres, SliceBuildConfig sc) 
        {
           // if (m_calibimage == null)  // blank image is null, create it
            {
                m_calibimage = new Bitmap(xres,yres);
                // fill it with black
                using (Graphics gfx = Graphics.FromImage(m_calibimage))
                using (SolidBrush brush = new SolidBrush(Color.Black))
                {
                    gfx.FillRectangle(brush, 0, 0, xres, yres);
                    int xpos = 0, ypos = 0;
                    Pen pen = new Pen(new SolidBrush(Color.Red));
                    for(xpos = 0; xpos < xres; xpos += (int)(sc.dpmmX*10.0))
                    {
                        Point p1 = new Point(xpos,0);
                        Point p2 = new Point(xpos,yres);
                        gfx.DrawLine(pen, p1, p2);
                    }
                    for (ypos = 0; ypos < yres; ypos += (int)(sc.dpmmY*10.0))
                    {
                        Point p1 = new Point(0, ypos);
                        Point p2 = new Point(xres, ypos);
                        gfx.DrawLine(pen, p1, p2);
                    }

                }
            }
            PrintLayer(m_calibimage, SLICE_CALIBRATION, SLICE_CALIBRATION);                    
        }
        public void ShowBlank(int xres, int yres) 
        {
            bool fillimage = false;
            if (m_blankimage == null)  // blank image is null, create it
            {
                fillimage = true;
                m_blankimage = new Bitmap(xres, yres);
            }
            else 
            {
                if (m_blankimage.Width != xres && m_blankimage.Height != yres) 
                {
                    fillimage = true;
                    m_blankimage = new Bitmap(xres, yres);
                }
            }
            if (fillimage) 
            {
                // fill it with black
                using (Graphics gfx = Graphics.FromImage(m_blankimage))
                using (SolidBrush brush = new SolidBrush(Color.Black))
                {
                    gfx.FillRectangle(brush, 0, 0, xres, yres);
                }            
            }
            PrintLayer(m_blankimage, SLICE_BLANK, SLICE_BLANK);            
        }
        /// <summary>
        /// This will return true while we are printing, even if paused
        /// </summary>
        public bool IsPrinting { get { return m_printing; } }

        private void RaiseStatusEvent(eBuildStatus status,string message) 
        {
            if (BuildStatus != null) 
            {
                BuildStatus(status,message);
            }
        }
        public bool IsPaused() 
        {
            return m_paused;
        }
        public void PausePrint() 
        {
            m_paused = true;
            m_state = STATE_IDLE;
            StopBuildTimer();
            RaiseStatusEvent(eBuildStatus.eBuildPaused,"Print Paused");
        }
        public void ResumePrint() 
        {
            m_paused = false;
            m_state = BuildManager.STATE_DO_NEXT_LAYER;
            StartBuildTimer();
            RaiseStatusEvent(eBuildStatus.eBuildResumed,"Next Layer");
        }

        // This function is called to start the print job
        public void StartPrint(SliceFile sf, GCodeFile gcode) 
        {
            if (m_printing)  // already printing
                return;

            m_printing = true;
            m_buildstarttime = new DateTime();
            m_buildstarttime = DateTime.Now;
            estimatedbuildtime = EstimateBuildTime(gcode);
            StartBuildTimer();
            
            m_sf = sf; // set the slicefile for rendering
            m_gcode = gcode; // set the file 
            m_state = STATE_START; // set the state machine as started
            m_runthread = new Thread(new ThreadStart(BuildThread));
            m_running = true;
            m_runthread.Start();
        }
        private static int getvarfromline(String line) 
        {
            try
            {
                int val = 0;
                line = line.Replace(';', ' '); // remove comments
                line = line.Replace(')', ' ');
                String[] lines = line.Split('>');
                if (lines[1].Contains("Blank"))
                {
                    val = -1; // blank screen
                }
                else 
                {
                    String []lns2 = lines[1].Trim().Split(' ');
                    val = int.Parse(lns2[0].Trim()); // first should be variable
                }
                
                return val;
            }
            catch (Exception ex) 
            {
                DebugLogger.Instance().LogError(line);
                DebugLogger.Instance().LogError(ex);
                return 0;
            }            
        }
        /*
         This is the thread that controls the build process
         * it needs to read the lines of gcode, one by one
         * send them to the printer interface,
         * wait for the printer to respond,
         * and also wait for the layer interval timer
         */
        void BuildThread() 
        {            
            int now = Environment.TickCount;
            int nextlayertime = 0;
            while (m_running)
            {
                try
                {
                    Thread.Sleep(0); // moved this sleep here for if the 
                    switch (m_state)
                    {
                        case BuildManager.STATE_START:
                            //start things off, reset some variables
                            RaiseStatusEvent(eBuildStatus.eBuildStarted, "Build Started");
                            m_state = BuildManager.STATE_DO_NEXT_LAYER; // go to the first layer
                            m_gcodeline = 0; // set the start line
                            m_curlayer = 0;
                            m_printstarttime = new DateTime();
                            break;
                        case BuildManager.STATE_WAITING_FOR_LAYER:
                            //check time var
                            if (Environment.TickCount >= nextlayertime)
                            {
                                m_state = BuildManager.STATE_DO_NEXT_LAYER; // move onto next layer
                            }
                            break;
                        case BuildManager.STATE_IDLE:
                            // do nothing
                            break;
                        case BuildManager.STATE_DO_NEXT_LAYER:
                            //check for done
                            if (m_gcodeline >= m_gcode.Lines.Length)
                            {
                                //we're done..
                                m_state = BuildManager.STATE_DONE;
                                continue;
                            }
                            string line = "";
                            // if the driver reports we're ready for the next command, or
                            // if we choose to ignore the driver ready status
                            if (UVDLPApp.Instance().m_deviceinterface.ReadyForCommand() || (UVDLPApp.Instance().m_appconfig.m_ignoreGCrsp == true))
                            {
                                // go through the gcode, line by line
                                line = m_gcode.Lines[m_gcodeline++];
                            }
                            else
                            {
                                continue; // device is not ready
                            }
                            line = line.Trim();
                            if (line.Length > 0) // if the line is not blank
                            {
                                // send  the line, whether or not it's a comment
                                // should check to see if the firmware is ready for another line

                                UVDLPApp.Instance().m_deviceinterface.SendCommandToDevice(line + "\r\n");
                                // if the line is a comment, parse it to see if we need to take action
                                if (line.Contains("<Delay> "))// get the delay
                                {
                                    nextlayertime = Environment.TickCount + getvarfromline(line);
                                    m_state = STATE_WAITING_FOR_LAYER;
                                    continue;
                                }
                                else if (line.Contains("<Slice> "))//get the slice number
                                {
                                    int layer = getvarfromline(line);
                                    int curtype = BuildManager.SLICE_NORMAL; // assume it's a normal image to begin with
                                    Bitmap bmp = null;

                                    if (layer == SLICE_BLANK)
                                    {
                                        if (m_blankimage == null)  // blank image is null, create it
                                        {
                                            m_blankimage = new Bitmap(m_sf.XProjRes, m_sf.YProjRes);
                                            // fill it with black
                                            using (Graphics gfx = Graphics.FromImage(m_blankimage))
                                            using (SolidBrush brush = new SolidBrush(Color.Black))
                                            {
                                                gfx.FillRectangle(brush, 0, 0, m_sf.XProjRes, m_sf.YProjRes);
                                            }
                                        }
                                        bmp = m_blankimage;
                                        curtype = BuildManager.SLICE_BLANK;
                                    }
                                    else
                                    {
                                        m_curlayer = layer;
                                        bmp = m_sf.GetSliceImage(m_curlayer); // get the rendered image slice or load it if already rendered                                    
                                        if (bmp == null) 
                                        {
                                            DebugLogger.Instance().LogError("Buildmanager bitmap is null layer = " + m_curlayer + " ");
                                        }
                                    }

                                    //raise a delegate so the main form can catch it and display layer information.
                                    if (PrintLayer != null)
                                    {
                                        PrintLayer(bmp, m_curlayer, curtype);
                                    }
                                }
                            }
                            break;
                        case BuildManager.STATE_DONE:
                            try
                            {
                                m_running = false;
                                m_state = BuildManager.STATE_IDLE;
                                StopBuildTimer();
                                DateTime endtime = new DateTime();
                                double totalminutes = (endtime - m_printstarttime).TotalMinutes;

                                m_printing = false; // mark printing doe
                                //raise done message
                                RaiseStatusEvent(eBuildStatus.eBuildStatusUpdate, "Build 100% Completed");
                                RaiseStatusEvent(eBuildStatus.eBuildCompleted, "Build Completed");
                            }
                            catch (Exception ex)
                            {
                                DebugLogger.Instance().LogError(ex.StackTrace);
                            }
                            break;
                    }
                }
                catch (Exception ex) 
                {
                    DebugLogger.Instance().LogError(ex.StackTrace);
                }
            }
        }


        public int GenerateTimeEstimate() 
        {
            return -1;
        }

        // This function manually cancels the print job
        public void CancelPrint() 
        {
            if (m_printing) // only if we're already printing
            {
                m_printing = false;
                StopBuildTimer();
                m_curlayer = 0;
                m_state = BuildManager.STATE_IDLE;
                m_running = false;
                RaiseStatusEvent(eBuildStatus.eBuildCancelled, "Build Cancelled");

            }
            m_paused = false;
        }
    }
}
