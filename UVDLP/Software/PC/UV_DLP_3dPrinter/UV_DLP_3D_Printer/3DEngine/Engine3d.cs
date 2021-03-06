using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Collections;
using Engine3D;
using UV_DLP_3D_Printer;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
namespace Engine3D
{
    public delegate void ModelAdded(Object3d model);
    public delegate void ModelRemoved(Object3d model);

    public class Engine3d
    {
        List<PolyLine3d> m_lines;
        public List<Object3d> m_objects;
        public event ModelAdded ModelAddedEvent;
        public event ModelRemoved ModelRemovedEvent;
        public bool m_alpha;

        public Engine3d() 
        {
            m_lines = new List<PolyLine3d>();
            m_objects = new List<Object3d>();
            m_alpha = false;
            //AddGrid(); // grid actually was created twice. -SHS
        }
        public void UpdateLists() 
        {
            foreach (Object3d obj in m_objects) 
            {
                obj.InvalidateList();
            }
        }
        public MinMax CalcSceneExtents() 
        {
            MinMax mm = new MinMax();
            try
            {
                int c = 0;
                foreach (Object3d obj in m_objects)
                {
                    obj.CalcMinMaxes();
                    if (c == 0) //first one
                    {                        
                        mm.m_min = obj.m_min.z;
                        mm.m_max = obj.m_max.z;
                    }
                    if (obj.m_min.z < mm.m_min)
                        mm.m_min = obj.m_min.z;

                    if (obj.m_max.z > mm.m_max)
                        mm.m_max = obj.m_max.z;
                    c++;
                }
            }
            catch (Exception ex) 
            {
                DebugLogger.Instance().LogError(ex.Message);
            }
            return mm;
        }

        public void UpdateGrid()
        {
            m_lines = new List<PolyLine3d>();
            AddGrid();
            AddPlatCube();
        }
        
        public void AddGridLine(int x1, int y1, int x2, int y2, Color col)
        {
            AddLine(new PolyLine3d(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0), col));
        }

        public void AddGrid() 
        {
            for (int x = -50; x < 51; x += 10)
            {
                AddGridLine(x, -50, x, 50, Color.Blue);
            }
            for (int y = -50; y < 51; y += 10)
            {
                AddGridLine(-50, y, 50, y, Color.Blue);
            }
            AddLine(new PolyLine3d(new Point3d(0, 0, -10), new Point3d(0, 0, 10), Color.Blue));

            // add XY arrows
            AddGridLine(50, 0, 58, 0, Color.Blue);
            AddGridLine(58, 0, 55, 3, Color.Blue);
            AddGridLine(58, 0, 55, -3, Color.Blue);
            AddGridLine(0, 50, 0, 58, Color.Blue);
            AddGridLine(0, 58, 3, 55, Color.Blue);
            AddGridLine(0, 58, -3, 55, Color.Blue);
            AddGridLine(60, 2, 66, -2, Color.Red);
            AddGridLine(60, -2, 66, 2, Color.Red);
            AddGridLine(0, 60, 0, 63, Color.Red);
            AddGridLine(0, 63, 2, 66, Color.Red);
            AddGridLine(0, 63, -2, 66, Color.Red);
        }
        //This function draws a cube the size of the build platform
        // The X/Y is centered along the 0,0 center point. Z extends from 0 to Z

        public void AddPlatCube() 
        {
            float platX, platY, platZ;
            float X, Y, Z;
            Color cubecol = Color.Gray;
            platX = (float)UVDLPApp.Instance().m_printerinfo.m_PlatXSize;
            platY = (float)UVDLPApp.Instance().m_printerinfo.m_PlatYSize;
            platZ = (float)UVDLPApp.Instance().m_printerinfo.m_PlatZSize;
            X = platX / 2;
            Y = platY / 2;
            Z = platZ;

            // bottom
            AddLine(new PolyLine3d(new Point3d(-X, Y, 0), new Point3d(X, Y, 0), cubecol));
            AddLine(new PolyLine3d(new Point3d(-X, -Y, 0), new Point3d(X, -Y, 0), cubecol));

            AddLine(new PolyLine3d(new Point3d(-X, -Y, 0), new Point3d(-X, Y, 0), cubecol));
            AddLine(new PolyLine3d(new Point3d( X, -Y, 0), new Point3d( X, Y, 0), cubecol));

            // Top
            AddLine(new PolyLine3d(new Point3d(-X, Y, Z), new Point3d(X, Y, Z), cubecol));
            AddLine(new PolyLine3d(new Point3d(-X, -Y, Z), new Point3d(X, -Y, Z), cubecol));

            AddLine(new PolyLine3d(new Point3d(-X, -Y, Z), new Point3d(-X, Y, Z), cubecol));
            AddLine(new PolyLine3d(new Point3d(X, -Y, Z), new Point3d(X, Y, Z), cubecol));

            // side edges
            AddLine(new PolyLine3d(new Point3d(X, Y, 0), new Point3d(X, Y, Z), cubecol));
            AddLine(new PolyLine3d(new Point3d(X, -Y, 0), new Point3d(X, -Y, Z), cubecol));

            AddLine(new PolyLine3d(new Point3d(-X, Y, 0), new Point3d(-X, Y, Z), cubecol));
            AddLine(new PolyLine3d(new Point3d(-X, -Y, 0), new Point3d(-X, -Y, Z), cubecol));


        
        }
        public void RemoveAllObjects() 
        {
            m_objects = new List<Object3d>();

        }
        public void AddObject(Object3d obj) 
        {
            m_objects.Add(obj); 
            if (ModelAddedEvent != null)
            {
                ModelAddedEvent(obj);
            }            
        }
        public void RemoveObject(Object3d obj) 
        {
            m_objects.Remove(obj);
            if (ModelRemovedEvent != null)
            {
                ModelRemovedEvent(obj);
            }                 
        }
        public void AddLine(PolyLine3d ply) { m_lines.Add(ply); }
        public void RemoveAllLines() 
        {
            m_lines = new List<PolyLine3d>();
        }

        public void RenderGL(bool alpha) 
        {

            try
            {
                GL.Enable(EnableCap.Lighting);
                GL.Enable(EnableCap.Light0);
                GL.Disable(EnableCap.LineSmooth);
                foreach (Object3d obj in m_objects)
                {
                    if (UVDLPApp.Instance().SelectedObject == obj) {
                        obj.RenderGL(alpha,true);
                    }
                    else 
                    {
                        obj.RenderGL(alpha, false);
                    }
                }
                GL.Disable(EnableCap.Lighting);
                GL.Disable(EnableCap.Light0);
                GL.Enable(EnableCap.LineSmooth);
                GL.LineWidth(1);
                foreach (PolyLine3d ply in m_lines)
                {
                    ply.RenderGL();
                }
                if (UVDLPApp.Instance().m_appconfig.m_showBoundingBox && (UVDLPApp.Instance().SelectedObjectList != null))
                {
                    GL.LineWidth(2);
                    Color clr = Color.Red;
                    foreach (Object3d obj in UVDLPApp.Instance().SelectedObjectList)
                    {
                        foreach (PolyLine3d ply in obj.m_boundingBox)
                        {
                            ply.m_color = clr;
                            ply.RenderGL();
                        }
                        clr = Color.Orange;
                    }
                }
            }
            catch (Exception) { }
        }

        public void RenderGL()
        {
            RenderGL(m_alpha);
        }

        /*
         This function takes the specified vector and intersects all objects
         * in the scene, it will return the polygon? or point that intersects first
         * We can expand this to return list of all intersections, for the initial
         * purposes of support generation, this is used to go from z=0 to z=platmaxz
         */
        public void RayCast(Point3d pstart, Point3d pend) 
        {
        
        }
    }
}
