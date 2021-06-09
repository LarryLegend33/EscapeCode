using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using NationalInstruments.Vision.Acquisition.Imaq;
using NationalInstruments.Vision;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Runtime.InteropServices;
using EMGU_Stimuli;
using EMGU_Keypress;
using System.Threading;
using System.IO.Ports;
using System.IO;
using System.Threading.Tasks.Dataflow;
using Microsoft.VisualBasic.FileIO;



// THINGS TO ADDRESS:
// WHEN EXTRACTING FROM THE BUFFER CONSECUTIVELY, DO YOU HAVE TO POINT TO THE MOST RECENT IMAGE ADDITIONS TO THE BUFFER? I THINK I MIGHT BE POINTING TO RANDOM IMAGES WHEN I START NEW FOR LOOPS INSTEAD OF THE MOST RECENT IMAGE. 


// NI-IMAQ library is for cameralink boards. NI-IMAQdx is for other interfaces. 

namespace NITest
{
    class Program
    {
        static List<byte[,]> imglist = new List<byte[,]>();
        static Mat modeimage_barrier = new Mat(new System.Drawing.Size(1280, 1024), Emgu.CV.CvEnum.DepthType.Cv8U, 1);
        static byte[,] imagemode = new byte[1024, 1280];
        static AutoResetEvent mode_reset = new AutoResetEvent(false);

        public class CamData
        {
            public Point fishcoord;
            public ContourProperties fishcont;
            public Mat roi;
            public uint buffernumber, jay;
            public byte[] pix_for_stim;
            public CamData(Mat roi_input, Point fishXY, ContourProperties cont, uint buffer, uint j_, byte[] stmpix)
            {
                jay = j_;
                fishcoord = fishXY;
                fishcont = cont;
                roi = roi_input;
                buffernumber = buffer;
                pix_for_stim = stmpix;
            }
        }

        static void Main(string[] args)
        {

            // Note that if you want to do halfmoon or stonehenge trials, place halfmoon and stonehenge in the center of the tank. 
            // Fill their center with a barrier for the first mode. Then take the barrier out and take the mode again. Use the smallest barrier possible (so the fish can get close to the center) and, like in nb trials, get rid of the tracking restriction on barriers  

            var options = new DataflowBlockOptions();
            options.BoundedCapacity = 10;
            var pipe_buffer = new BufferBlock<CamData>(options);
            Point tank_center = new Point
            {
                X = 640,
                Y = 512,
            };
            int roidim = 80;

            string camera_id = "img0"; //this is the ID of the NI-IMAQ board in NI MAX. 
            var _session = new ImaqSession(camera_id);
            bool reuse_background = false;
            bool drew_barriers = false;
            bool halfmoon = false;
            bool stonehenge = false;
            bool minefield = false;
            bool minefield_control = false;
            Console.WriteLine("Enter FishID   ");
            String fishid = Console.ReadLine();
            String home_directory = "C:/Users/Deadpool/Desktop/Results/";
            String exp_directory = home_directory + fishid;
            bool exists_already = System.IO.Directory.Exists(exp_directory);
            if (!exists_already)
            {
                System.IO.Directory.CreateDirectory(exp_directory);
            }
            else
            {
                Console.WriteLine("Directory Already Exists. Overrite?  ");
                String overwrite = Console.ReadLine();
                if (overwrite == "y")
                {
                    System.IO.Directory.CreateDirectory(exp_directory);
                }
                else if (overwrite == "c") { }
                else
                {
                    Environment.Exit(0);
                }
            }
            Console.WriteLine("Enter Light X Location  ");
            String lightloc_X = Console.ReadLine();
            Console.WriteLine("Enter Light Y Location  ");
            String lightloc_Y = Console.ReadLine();
            int light_location_X = Convert.ToInt32(lightloc_X) - 25;
            int light_location_Y = Convert.ToInt32(lightloc_Y);
            Console.WriteLine("Enter Experiment Type  ");
            String exp_string = Console.ReadLine();
            Console.WriteLine("Use old background?  ");
            String reuse = Console.ReadLine();
            if (reuse == "y")
            {
                reuse_background = true;
            }
            if (exp_string == "n" || exp_string == "t" || exp_string == "v")
            {
                minefield_control = true;
            }
            else if (exp_string == "b")
            {
                minefield = true;
            }
            String camerawindow = "Camera Window";
            CvInvoke.NamedWindow(camerawindow);
            int frameWidth = 1280;
            int frameHeight = 1024;
            uint bufferCount = 3;
            // Could try changing this to 2 or 100 
            // Checked and there is no card memory. It makes a buffer on system mem. Tried increasing virtual memory so 
            // HD can be used as RAM. Allocated an additional 32 GB to virtual mem. 
            uint buff_out = 0;
            int numchannels = 1;
            MCvScalar gray = new MCvScalar(128, 128, 128);
            List<ContourProperties> barrierlist = new List<ContourProperties>();
            ContourProperties fishcontour = new ContourProperties();
            ContourProperties fishcontour_correct = new ContourProperties();
            ContourProperties barrier = new ContourProperties();
            System.Drawing.Size framesize = new System.Drawing.Size(frameWidth, frameHeight);
            System.Drawing.Size roi_size = new System.Drawing.Size(roidim, roidim);
            Mat cvimage = new Mat(framesize, Emgu.CV.CvEnum.DepthType.Cv8U, numchannels);
            Mat modeimage_barrier_roi = new Mat(roi_size, Emgu.CV.CvEnum.DepthType.Cv8U, numchannels);
            Mat modeimage = new Mat(framesize, Emgu.CV.CvEnum.DepthType.Cv8U, numchannels);
            //            Mat modeimage_barrier = new Mat(framesize, Emgu.CV.CvEnum.DepthType.Cv8U, numchannels);
            Mat maxproj_cv = new Mat(framesize, Emgu.CV.CvEnum.DepthType.Cv8U, numchannels);
            AutoResetEvent event1 = new AutoResetEvent(true);
            AutoResetEvent event2 = new AutoResetEvent(false);
            MCvMoments COM= new MCvMoments();
            byte[,] data_2D = new byte[frameHeight, frameWidth];
            byte[,] data_2D_roi = new byte[roidim, roidim];
            byte[,] imagemode_nobarrier = new byte[frameHeight, frameWidth];
            byte[,] maxprojimage = new byte[frameHeight, frameWidth];
            ImaqBuffer image = null;
            ImaqBufferCollection buffcollection = _session.CreateBufferCollection((int)bufferCount, ImaqBufferCollectionType.VisionImage);
            _session.RingSetup(buffcollection, 0, false);
            _session.Acquisition.AcquireAsync();
            RecordAndStim experiment = new RecordAndStim(event1, event2, pipe_buffer, exp_string);
            experiment.experiment_directory = exp_directory;
            var stimthread = new Thread(experiment.StartStim);
            stimthread.Start();

            // THIS GRABS THE MODE FOR THE TANK IN GENERAL BEFORE ALIGNMENT

            if (!experiment.alignment_complete)
            {
                CvInvoke.WaitKey(0);
                imglist = GetImageList(_session, 500, 10);
                maxprojimage = FindMaxProjection(imglist);
                maxproj_cv.SetTo(maxprojimage);
                imglist.Clear();
                CvInvoke.Imshow(camerawindow, maxproj_cv);
                CvInvoke.WaitKey(0);
            }

            // IF CAMERA IS NOT YET ALIGNED TO THE PROJECTOR, THIS LOOP FINDS THE LOCATION OF THE CALIBRATION CONTOUR THE EXPERIMENT CLASS IS PLACING ON THE PROJECTOR.

            experiment.start_align = true;
            if (!experiment.alignment_complete)
            {
                while (!experiment.alignment_complete)
                {
                    imglist = GetImageList(_session, 500, 10);
                    data_2D = FindMaxProjection(imglist);
                    cvimage.SetTo(data_2D);
                    Console.WriteLine("Finding Largest Contour");
                    experiment.projcenter_camcoords = LargestContour(cvimage, maxproj_cv, true).center;
                    CvInvoke.Imshow(camerawindow, cvimage);
                    CvInvoke.WaitKey(1);
                    event2.Set();
                    event1.WaitOne();
                }
                imglist.Clear();
                CvInvoke.WaitKey(0);
                imglist = GetImageList(_session, 500, 10);
                data_2D = FindMaxProjection(imglist);
                cvimage.SetTo(data_2D);
                experiment.tankwidth = LargestContour(cvimage, maxproj_cv, true).height * 2;
                Console.WriteLine("Width Of Tank Contour");
                Console.WriteLine(experiment.tankwidth);
                CvInvoke.Imshow(camerawindow, cvimage);
                CvInvoke.WaitKey(0);
                imglist.Clear();

            }

            // Next, the opposite thread is going to display a black circle that is the same size as the tank. Do a max projection on this
            // contour in order to measure width of the tank in projector coordinates.


            // Now you've put the IR filter back over the camera and are ready to do an experiment.             
            // Get mode of image with no barrier present so you can background subtract and find the barriers and fish.  
            imglist.Clear();
            if (reuse_background)
            {
                modeimage = CvInvoke.Imread(home_directory + "/background_nobar" + exp_string + ".tif", 0);
            }
            else
            {
                imglist = GetImageList(_session, 5000, 400);
                imagemode_nobarrier = FindMode(imglist);
                modeimage.SetTo(imagemode_nobarrier);
                imglist.Clear();
                CvInvoke.Imshow(camerawindow, modeimage);
                CvInvoke.WaitKey(0);
            }

            // Here you have just added barriers to the tank. Now get a new mode that contains the barriers for use in background subtraction to find fish 
            // and for localizing barriers. 

            if (halfmoon || stonehenge || minefield)
            {
                imglist = GetImageList(_session, 5000, 400);
                if (reuse_background)
                {

                    modeimage_barrier = CvInvoke.Imread(home_directory + "/background_" + exp_string + ".tif", 0);
                }
                else
                {
                    imagemode = FindMode(imglist);
                    modeimage_barrier.SetTo(imagemode);
                }

                modeimage_barrier.Save(exp_directory + "/background_" + exp_string + ".tif");
                imglist.Clear();
                barrierlist = BarrierLocations(modeimage_barrier, modeimage);
                for (int ind = 0; ind < barrierlist.Count; ind++)
                {
                    experiment.barrier_position_list.Add(barrierlist[ind].center);
                    experiment.barrier_radius_list.Add(barrierlist[ind].height / 2);
                }
            }
            else if (minefield_control)
            {
                modeimage_barrier.SetTo(imagemode_nobarrier);
                modeimage_barrier.Save(exp_directory + "/background_" + exp_string + ".tif");

                barrierlist = GenerateVirtualBarriers(experiment.tankwidth, tank_center.X, tank_center.Y);
                for (int ind = 0; ind < barrierlist.Count; ind++)
                {
                    experiment.barrier_position_list.Add(barrierlist[ind].center);
                    experiment.barrier_radius_list.Add(barrierlist[ind].height / 2);
                }
            }

            using (StreamWriter barrierfile = new StreamWriter(exp_directory + "/barrierstruct_" + exp_string + ".txt"))
            {

                for (int bar = 0; bar < barrierlist.Count; bar++)
                {
                    if (bar == 0)
                    {
                        barrierfile.WriteLine(experiment.templatewidth.ToString());
                        barrierfile.WriteLine(experiment.tankwidth.ToString());
                    }
                    barrierfile.WriteLine(barrierlist[bar].center.ToString());
                    barrierfile.WriteLine(barrierlist[bar].height.ToString());
                }
            }

            CvInvoke.Imshow(camerawindow, modeimage_barrier);
            CvInvoke.WaitKey(0);


            if (halfmoon) //THIS IS BECAUSE YOU TAKE THE BARRIER AWAY AFTER IT FINDS THE HOLE. IE FOR HALFMOON TRIALS, YOU FIRST KEEP THE HALFMOON THERE FOR MODEIMAGE, THEN ADD A BARRIER THE SIZE OF THE HOLE FOR FINDING OF THE HOLE OF THE BARRIER. IF YOU WANT TO RUN STONEHENGE OR HALFMOON, DECLARE MINEFIELD_CONTROL AS TRUE, but don't draw barriers. 
            {
                modeimage_barrier = modeimage;
                imagemode = imagemode_nobarrier;
            }


            // IMAGE ACQUISITION AND FISH FINDING. 
            //            Idea is to first acquire the image and turn it into a cvimage matrix. find the fish by finding the largest contour on a background subtracted and thresholded image (LargestContour function).  Each time you find the fish, store its coords so you can just search within a small ROI on the next frame. If you lose the fish, go back out to full frame and find it again. 
            Point f_center = new Point();
            Mat cv_roi = new Mat(roi_size, Emgu.CV.CvEnum.DepthType.Cv8U, numchannels);
            image = _session.Acquisition.Extract((uint)0, out buff_out);
            uint j = buff_out;
            int experiment_phase = 0;
            int xycounter = 0;
            Console.WriteLine("j followed by buff_out");
            Console.WriteLine(j.ToString());
            Console.WriteLine(buff_out.ToString());
            List<Point> coordlist = new List<Point>();
            List<int> phasebounds = new List<int>();
            while (true)
            {
                if (mode_reset.WaitOne(0))
                {
                    Console.WriteLine("Clearing Imagelist");
                    imglist.Clear();
                    mode_reset.Reset();
                }
                image = _session.Acquisition.Extract(j, out buff_out);
                try
                {
                    data_2D = image.ToPixelArray().U8;
                }
                catch (NationalInstruments.Vision.VisionException e)
                {
                    Console.WriteLine(e);
                    continue;
                }

                byte[] stim_pixel_readout = new byte[100];
                for (int pix = 0; pix < 100; pix++)
                {
                    stim_pixel_readout[pix] = data_2D[light_location_Y, light_location_X + pix];
                }
                cvimage.SetTo(data_2D);
                fishcontour = FishContour(cvimage, modeimage_barrier, tank_center, barrierlist, minefield_control);

                // com makes sure that the head is near the barrier. 
                if (fishcontour.height != 0)
                {
                    fishcontour_correct = fishcontour;
                    f_center.X = fishcontour.com.X;
                    f_center.Y = fishcontour.com.Y;
                }
                if (!experiment.stim_in_progress)
                {
                    drew_barriers = false;
                }
                if (experiment.stim_in_progress && !drew_barriers)
                {
                    if (halfmoon || stonehenge || minefield || minefield_control)
                    {
                        for (int ind = 0; ind < barrierlist.Count; ind++)
                        {
                            CvInvoke.Circle(cvimage, barrierlist[ind].center, barrierlist[ind].height / 2, new MCvScalar(255, 0, 0), 1);
                        }
                    }
                    Image<Gray, Byte> d2d = cvimage.ToImage<Gray, Byte>();
                    data_2D_roi = SliceROIImage(d2d, f_center.X, f_center.Y, roidim);
                    drew_barriers = true;
                }
                else
                {
                    data_2D_roi = SliceROI(data_2D, f_center.X, f_center.Y, roidim);
                }
                cv_roi = new Mat(roi_size, Emgu.CV.CvEnum.DepthType.Cv8U, numchannels);
                cv_roi.SetTo(data_2D_roi);

                CamData camdat = new CamData(cv_roi, f_center, fishcontour_correct, buff_out, j, stim_pixel_readout);
                pipe_buffer.Post(camdat);
                if (j % 10 == 0)
                {
                    xycounter++;
                    coordlist.Add(camdat.fishcoord);
                    if (experiment.experiment_phase > experiment_phase)
                    {
                        experiment_phase = experiment.experiment_phase;
                        phasebounds.Add(xycounter);
                    }
                }
                if (j % 100 == 0 && !experiment.stim_in_progress)
                {
                    //    CvInvoke.Circle(cvimage, fishcontour_correct.center, 2,new MCvScalar(255, 255, 0)); 
                    CvInvoke.Circle(cvimage, fishcontour_correct.com, 2, new MCvScalar(255, 255, 255));
                    if (halfmoon || stonehenge || minefield || minefield_control)
                    {
                        for (int ind = 0; ind < barrierlist.Count; ind++)
                            CvInvoke.Circle(cvimage, barrierlist[ind].center, barrierlist[ind].height / 2, new MCvScalar(255, 0, 0), 3);
                    }
                    else
                    {
                        CvInvoke.Circle(cvimage, experiment.barrier_center, barrier.height / 2, new MCvScalar(255, 0, 0), 3);
                    }
                    CvInvoke.Imshow(camerawindow, cvimage);
                    CvInvoke.WaitKey(1);
                    if (j % 1000 == 0)
                    {
                        byte[,] mode_frame = new byte[frameHeight, frameWidth];
                        Buffer.BlockCopy(data_2D, 0, mode_frame, 0, data_2D.Length);
                        imglist.Add(mode_frame);
                        if (imglist.LongCount() == 40)
                        {
                            var modethread = new Thread(() => ModeWrapper(imglist, mode_reset, experiment, exp_directory));
                            modethread.Start();
                        }
                    }
                }
                if (experiment.experiment_complete)
                {
                    break;
                }

                j = buff_out + 1;
            }
            modeimage_barrier.Save(home_directory + "/background_" + exp_string + ".tif");
            modeimage.Save(home_directory + "/background_nobar" + exp_string + ".tif");
            string experiment_string = exp_directory + "/all_xycoords_" + exp_string + ".txt";
            string phasestring = exp_directory + "/phase_" + exp_string + ".txt";
            string numframes_gray = exp_directory + "/numframesgray_" + exp_string + ".txt";
            string numframes_gray_dark = exp_directory + "/numframesgray_dark.txt";
            using (StreamWriter sr = new StreamWriter(experiment_string))
            {
                foreach (Point fishpoint in coordlist)
                {
                    sr.WriteLine(fishpoint.ToString());
                }
            }
            using (StreamWriter sr = new StreamWriter(phasestring))
            {
                foreach (int phase in phasebounds)
                {
                    sr.WriteLine(phase.ToString());
                }
            }
            using (StreamWriter sr = new StreamWriter(numframes_gray))
            {
                foreach (int ng in experiment.num_grayframes)
                {
                    sr.WriteLine(ng.ToString());
                }
            }
            if (exp_string == "b")
            {
                using (StreamWriter sr = new StreamWriter(numframes_gray_dark))
                {
                    foreach (int ngd in experiment.num_grayframes_d)
                    {
                        sr.WriteLine(ngd.ToString());
                    }
                }
            }




    }

        static ContourProperties FishContour(Mat image_raw, Mat background, Point tc, List<ContourProperties> blist, bool control)
        {

// BUG IN HERE IS THAT CONTPROPS HEIGHT GETS SET EVEN WHEN THERE IS NO CONTOUR FOUND. THIS OCCURS BEFORE ENTERING THE LOOP
// BASED ON CONTRPOPS.HEIGHT SIZE. YOU RETURN SOMETHING WITH A HEIGHT BUT NO COORDINATE (0,0) AND THE MAIN LINE THINKS YOU HAVE A CONTOUR AT 0,0. 
            bool fishcont_found = false;
            Size frsize = new Size(image_raw.Width, image_raw.Height);
            Mat image = new Mat(frsize, Emgu.CV.CvEnum.DepthType.Cv8U, 1);
            ContourProperties contprops = new ContourProperties();
            ThresholdType ttype = 0;
            VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();
            Mat hierarchy = new Mat();
            CvInvoke.AbsDiff(image_raw, background, image);
            // This should be 30 as the LB. Switched to 20 to see if i could pick up paramecia. 
            CvInvoke.Threshold(image, image, 25, 255, ttype);
            // IF YOU NEED TO SHOW THE THRESHOLDED IMAGE, UNCOMMENT THESE LINES
//            String camerawindow2 = "Camera Window 2";
  //          CvInvoke.NamedWindow(camerawindow2);
    //        CvInvoke.Imshow(camerawindow2, image);
      //      CvInvoke.WaitKey(1);
            CvInvoke.FindContours(image, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxNone);
            int fish_contour_index = 0;
            int height = 0;
            Point contourCOM = new Point();
            Point contour_center = new Point();
            Rectangle bounding_rect = new Rectangle();
            for (int ind = 0; ind < contours.Size; ind++)
            {
                MCvMoments com = new MCvMoments();
                com = CvInvoke.Moments(contours[ind]);
                contourCOM.X = (int)(com.M10 / com.M00);
                contourCOM.Y = (int)(com.M01 / com.M00);
                bounding_rect = CvInvoke.BoundingRectangle(contours[ind]);
                contour_center.X = (int)(bounding_rect.X + (float)bounding_rect.Width / (float)2);
                contour_center.Y = (int)(bounding_rect.Y + (float)bounding_rect.Height / (float)2);
                if (bounding_rect.Width > bounding_rect.Height)
                {
                    height = bounding_rect.Width;
                }
                else
                {
                    height = bounding_rect.Height;
                }
                if (height < 60 && height > 8)
                {
                    if (image_raw.Width > 1000)
                    {
                        if (!control)
                        {
                            bool tooclose = false;
                            for (int i = 0; i < blist.Count; i++)
                            {

// This allows 3, 4, or 5 to be recorded as a COM center, but would be rejected by Tap 
                                if (VectorMag(blist[i].center, contourCOM) - (blist[i].height / 2) < 3)
                                {                               
                                    tooclose = true;
                                    break;
                                }
                            }
                            if (tooclose)
                            {
                                continue;
                            }
                        }
                        if (VectorMag(contourCOM, tc) > 460)
                        //this tells the algorithm not to look for fish outside the tank. 
                        {
                            continue;
                        }
                        if (contourCOM.X < 0 || contourCOM.Y < 0)
                        {
                            continue;
                        }
                    }
                    fish_contour_index = ind;                    
                    fishcont_found = true;
                    break;
                }
            }
            if (fishcont_found)
            {
// could also choose the contour center below using the bounding rect
                contprops.com = contourCOM;
                contprops.height = height;
                contprops.center = contour_center;        
            }
            return contprops;
        }

       static byte[,] SliceROIImage(Image<Gray, Byte> rawdata, int centerX,int centerY, int dimension) {
            byte[,] roi = new byte[dimension, dimension];
            int roirow = 0;
            int roicol = 0;
            int half_roi_dim = dimension / 2;
            for (int rowind = centerY - half_roi_dim; rowind < centerY + half_roi_dim; rowind++)
            { 
                for (int colind = centerX - half_roi_dim; colind < centerX + half_roi_dim; colind++)
                {
                    if (rowind >= 0 && colind >= 0 && rowind < rawdata.Width && colind < rawdata.Height)
                    {
                        roi[roirow, roicol] = rawdata.Data[rowind, colind, 0];
                    }
                    else
                    {
                        roi[roirow, roicol] = 0;
                    }
                    roicol++;
                }
                roirow++;
                roicol = 0;
            }
            return roi;
            }



        static byte[,] SliceROI(byte[,] rawdata,int centerX,int centerY, int dimension)
        {
            byte[,] roi = new byte[dimension, dimension];
            int roirow = 0;
            int roicol = 0;
            int half_roi_dim = dimension / 2;
            for (int rowind = centerY - half_roi_dim; rowind < centerY + half_roi_dim; rowind++)
            { 
                for (int colind = centerX - half_roi_dim; colind < centerX + half_roi_dim; colind++)
                {
                    if (rowind >= 0 && colind >= 0 && rowind < rawdata.GetLength(0) && colind < rawdata.GetLength(1))
                    {
                        roi[roirow, roicol] = rawdata[rowind, colind];
                    }
                    else
                    {
                        roi[roirow, roicol] = 0;
                    }
                    roicol++;
                }
                roirow++;
                roicol = 0;
            }
            return roi;
        }

        public struct ContourProperties
        {
            public Point center;
            public int height;
            public int width;
            public Point com;
        }

// Goal of this function is to place barriers halfway between the edge of the tank and the center. 
        static List<ContourProperties> GenerateVirtualBarriers(int tankwidth, int tc_x, int tc_y)
        {
            double vb_distance_from_center = tankwidth / 4;
            int barrier_diam = 110;
            int vb_arm = Convert.ToInt32(Math.Sqrt(Math.Pow(vb_distance_from_center, 2) / 2));
            int how_many_vbs = 4;
            List<ContourProperties> vb_list = new List<ContourProperties>();
            Point virtualCenter = new Point();
            for (int vb_ind = 0; vb_ind < how_many_vbs; vb_ind++)
            {
                ContourProperties contprops = new ContourProperties();
                if (vb_ind == 0)
                {
                    virtualCenter.X = tc_x + vb_arm;
                    virtualCenter.Y = tc_y + vb_arm;
                }
                if (vb_ind == 1)
                {
                    virtualCenter.X = tc_x + vb_arm;
                    virtualCenter.Y = tc_y - vb_arm;
                }
                if (vb_ind == 2)
                {
                    virtualCenter.X = tc_x - vb_arm;
                    virtualCenter.Y = tc_y + vb_arm;
                }
                if (vb_ind == 3)
                {
                    virtualCenter.X = tc_x - vb_arm;
                    virtualCenter.Y = tc_y - vb_arm;
                }
                contprops.center = virtualCenter;
                contprops.height = barrier_diam;
                vb_list.Add(contprops);
            }
            return vb_list;
        }

        static List<ContourProperties> BarrierLocations(Mat image_raw, Mat background)
        {
            int minArea = 1000;
            int maxArea = 600000;
            Size frsize = new Size(image_raw.Width, image_raw.Height);
            Mat image = new Mat(frsize, Emgu.CV.CvEnum.DepthType.Cv8U, 1);

            ThresholdType ttype = 0;
            VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();
            List<VectorOfPoint> contlist = new List<VectorOfPoint>();
            List<ContourProperties> cp_list = new List<ContourProperties>();
            Mat hierarchy = new Mat();
            CvInvoke.AbsDiff(image_raw, background, image);
            CvInvoke.Threshold(image, image, 50, 255, ttype);
            CvInvoke.FindContours(image, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxNone);
            Point contourCenter = new Point();

            for (int ind = 0; ind < contours.Size; ind++)
            {
                double area = CvInvoke.ContourArea(contours[ind]);
                if(area > minArea && area < maxArea)
                {
                    contlist.Add(contours[ind]);
                }
            }
            for (int contind = 0; contind < contlist.Count; contind++)
            {
                ContourProperties contprops = new ContourProperties();
                Rectangle bounding_rect = CvInvoke.BoundingRectangle(contlist[contind]);
                contourCenter.X = (int)(bounding_rect.X + (float)bounding_rect.Width / (float)2);
                contourCenter.Y = (int)(bounding_rect.Y + (float)bounding_rect.Height / (float)2);
                contprops.center = contourCenter;
                contprops.height = bounding_rect.Height;
                cp_list.Add(contprops);              
            }

            return cp_list;

        }

        static ContourProperties LargestContour(Mat image_raw, Mat background, bool draw)
        {

            Size frsize = new Size(image_raw.Width, image_raw.Height);
            Mat image = new Mat(frsize, Emgu.CV.CvEnum.DepthType.Cv8U, 1);
            ContourProperties contprops = new ContourProperties();
            ThresholdType ttype = 0;
            VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();
            Mat hierarchy = new Mat();
            CvInvoke.AbsDiff(image_raw, background, image);
            CvInvoke.Threshold(image, image, 35, 255, ttype);
            CvInvoke.FindContours(image, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxNone);
            double largest_area = 0;
            int largest_area_index = 0;
            for (int ind = 0; ind < contours.Size; ind++)
            {
                double area = CvInvoke.ContourArea(contours[ind]);
                if (area > largest_area)
                {
                    if (image_raw.Width > 1000 && contours[ind][0].Y < 100) // prevents stim LED from being caught as a contour
                    {
                        continue;
                    }
                    largest_area = area;
                    largest_area_index = ind;
                }
            }
            var contourCenter = new Point();
            if (contours.Size > 0)
            {
                Rectangle bounding_rect = CvInvoke.BoundingRectangle(contours[largest_area_index]);
                contourCenter.X = (int)(bounding_rect.X + (float)bounding_rect.Width / (float)2);
                contourCenter.Y = (int)(bounding_rect.Y + (float)bounding_rect.Height / (float)2);
                contprops.center = contourCenter;
                contprops.height = bounding_rect.Height;
                if (draw)
                {
                    CvInvoke.DrawContours(image_raw, contours, largest_area_index, new MCvScalar(255, 0, 0), 2); // these are correct. 
                    CvInvoke.Rectangle(image_raw, bounding_rect, new MCvScalar(255, 0, 0));
                    CvInvoke.Circle(image_raw, contourCenter, 50, new MCvScalar(255, 0, 0));  // THIS IS ABOUT 50 PIXELS TOO HIGH
                }
            }
            else
            {
            //    Console.WriteLine("no contours");
            }
            return contprops;
        }


// ON MOST RECENT BUFF OUT AFTER CALIBRATION, ASK FOR NEXT BUFFER TO START. 

        static List<byte[,]> GetImageList(ImaqSession ses, int numframes, int mod) { 
     

            int frheight = 1024;
            int frwidth = 1280;
            List<byte[,]> avg_imglist = new List<byte[,]>();
            byte[,] avg_data_2D = new byte[frheight, frwidth];
            uint buff_out = 0;
            ImaqBuffer image = null;
            for (uint i = 0; i < numframes; i++)
            {
                image = ses.Acquisition.Extract((uint)0, out buff_out);
                avg_data_2D = image.ToPixelArray().U8;
                if (i % mod == 0)
                {
                    byte[,] avgimage_2D = new byte[frheight, frwidth];
                    Buffer.BlockCopy(avg_data_2D, 0, avgimage_2D, 0, avg_data_2D.Length);
                    avg_imglist.Add(avgimage_2D);
                }
            }
            return avg_imglist;
        }


        public static void ModeWrapper(List<byte[,]> md_images, AutoResetEvent md_reset, RecordAndStim exp, String exp_d)
        {

// Take first X values of list if you think mode calc will take longer than the next addition to the list
            Console.WriteLine("ModeWrapper Called");
            // List<byte[,]> first120 = md_images.Take(500).ToList();
            List<byte[,]> mdlist = md_images.Take(40).ToList();
            imagemode = FindMode(mdlist);
  //          imagemode = FindMode(md_images);
            modeimage_barrier.SetTo(imagemode);
            modeimage_barrier.Save(exp_d + "/background_" + exp.trialnumber.ToString("D2") + "_" + exp.condition[0] + ".tif");
            md_reset.Set();
        }

        static byte[,] FindMode(List<byte[,]> backgroundimages)
        {
            byte[,] image = backgroundimages[0];
            byte[,] output = new byte[image.GetLength(0), image.GetLength(1)];
            uint[] pixelarray = new uint[backgroundimages.Count];
            for (int rowind = 0; rowind < image.GetLength(0); rowind++)
            {
                for (int colind = 0; colind < image.GetLength(1); colind++)
                {
                    int background_number = 0;
                    foreach (byte[,] background in backgroundimages)
                    {
                   //     if (rowind == colind && rowind % 100 == 0)
                     //   {
                    //        Console.WriteLine(background[rowind, colind]); // This gives pixel vals of a line down the diagonal of the images for all images in modelist. //Values are unique indicating that the copy method is working.  
                     //   }
                        pixelarray[background_number] = background[rowind, colind];
                        // get mode of this. enter it as the value in output. 
                        background_number++;
                    }
                    uint mode = pixelarray.GroupBy(i => i)
                              .OrderByDescending(g => g.Count())
                              .Select(g => g.Key)
                              .First();
                    output[rowind, colind] = (byte)mode;

                }
            }
            Console.WriteLine("Done");
            return output;
        }

        static double VectorMag(Point fc, Point reference)
        {
            double vecmag = Math.Sqrt((fc.X - reference.X) * (fc.X - reference.X) + (fc.Y - reference.Y) * (fc.Y - reference.Y));
            return vecmag;
        }

        static byte[,] FindMaxProjection(List<byte[,]> backgroundimages)
        {

            byte[,] image = backgroundimages[0];
            byte[,] output = new byte[image.GetLength(0), image.GetLength(1)];
            uint[] pixelarray = new uint[backgroundimages.Count];
            for (int rowind = 0; rowind < image.GetLength(0); rowind++)
            {
                for (int colind = 0; colind < image.GetLength(1); colind++)
                {
                    int background_number = 0;
                    foreach (byte[,] background in backgroundimages)
                    {
                        pixelarray[background_number] = background[rowind, colind];
                        background_number++;
                    }
                    uint max = pixelarray.Max();
                    output[rowind, colind] = (byte)max;                
                }
            }
            Console.WriteLine("Done");
            return output;
        }
    }
    }



