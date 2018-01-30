using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Diagnostics;
using Thorlabs.MotionControl.DeviceManagerCLI;
using Thorlabs.MotionControl.FilterFlipperCLI;
using Thorlabs.MotionControl.GenericMotorCLI;
using Thorlabs.MotionControl.GenericMotorCLI.ControlParameters;
using Thorlabs.MotionControl.GenericMotorCLI.AdvancedMotor;
using Thorlabs.MotionControl.GenericMotorCLI.Settings;
using Thorlabs.MotionControl.FilterFlipper;
using System.Threading.Tasks.Dataflow;
using NITest;


namespace EMGU_Stimuli
{

    class RecordAndStim
    {
        public Point current_barrier_loc, virtual_barrier_center,fish_center, barrier_center,projcenter_camcoords, tankcenter;
        public volatile bool start_align, experiment_running, alignment_complete, centering_success, minefield, minefield_control, darkness, memory, stim_in_progress, motor_closed, experiment_complete, pre_escape, start_state_light;
        public volatile int trialnumber, tankwidth, barrier_radius,templatewidth,threshold_radius,threshold_multiplier, number_of_trials, stim_pixel_readout, freerun_mins, escape_mins, crossing_thresh, experiment_phase, walltrial;
        public volatile string experiment_type, experiment_directory;
        float looming_diam, looming_size_thresh, growth_rate;
        public Mat roi;
        PointF stimcenter;
        Point projector_center, camera_center;
        Mat img;
        Stopwatch experiment_timer = new Stopwatch();
        FilterFlipper mymotor;
        String serialnum = "";
        String win1,stimtype;
        AutoResetEvent event1, event2;
        SerialPort pyboard = new SerialPort("COM6",115200);
        public List<int> barrier_radius_list;
        public List<Point> barrier_position_list;
        BufferBlock<NITest.Program.CamData> pipe_buffer;

        public RecordAndStim(AutoResetEvent event_1, AutoResetEvent event_2, String stim_type, BufferBlock<NITest.Program.CamData> buffblock)
        {    
            stim_in_progress = false;
            experiment_complete = false;
            pipe_buffer = buffblock;
            barrier_position_list = new List<Point>();
            barrier_radius_list = new List<int>();
            pre_escape = true;
            walltrial = 1;
            number_of_trials = 10;
            threshold_multiplier = 3;
            threshold_radius = 40;
            freerun_mins = 5;
            escape_mins = 60;
            crossing_thresh = 2;
            experiment_phase = 0;

            // CHANGING THESE 3 THINGS CHANGES WHETHER THE PROGRAM CALIBRATES OR NOT. 
            alignment_complete = true;
            projector_center = new Point
            {
                X = 1192,
                Y = 462,
            };
            tankwidth = 952;
            //

            roi = new Mat(new Size(80,80), Emgu.CV.CvEnum.DepthType.Cv8U, 1);
            stimtype = stim_type;
            centering_success = true;
            tankcenter = new Point
            {
                X = 640,
                Y = 512,
            };
            barrier_radius = 75;
            fish_center.X = 0;            
            fish_center.Y = 0;
            trialnumber = 1;
            templatewidth = 774;
            barrier_center.X = tankcenter.X;
            barrier_center.Y = tankcenter.Y;
            virtual_barrier_center.X = 0;
            virtual_barrier_center.Y = 0;
            current_barrier_loc.X = 0;
            current_barrier_loc.Y = 0;
            start_align = false;
            experiment_running = true;
            // MFF102 Control. State 2 is up, state 1 is sideways
            DeviceManagerCLI.BuildDeviceList();
            List <string> serialnums = DeviceManagerCLI.GetDeviceList(FilterFlipper.DevicePrefix);
            mymotor = FilterFlipper.CreateFilterFlipper(serialnums[0]);
            mymotor.Connect(serialnums[0]);
            mymotor.WaitForSettingsInitialized(5000);
            mymotor.StartPolling(250);
            if(mymotor.Position == 2)
            {
                mymotor.SetPosition(1, 1000);
            }                  
            looming_diam = 1;
            looming_size_thresh = 60;
            growth_rate = 1.01f;
            event1 = event_1;
            event2 = event_2;
            if (minefield)
            {
                start_state_light = true;
            }
            else if(darkness)
            {
                start_state_light = false;
            }
            projcenter_camcoords = new Point
            {
                X = 100,
                Y = 100,
            };
            
            camera_center = new Point
            {
                X = 640,
                Y = 512,
            };
       //     img = new Mat(1280, 1024, DepthType.Cv8U, 3); //Create a 3 channel image 
            img = new Mat(2000, 2000, DepthType.Cv8U, 3); //Create a 3 channel image 
            win1 = "Stimulus Window";
            pyboard.Open();
            pyboard.WriteLine("import escape_rig\r");
            pyboard.WriteLine("escape_rig.ir.low()\r");
            pyboard.WriteLine("escape_rig.tap.low()\r");
            pyboard.WriteLine("escape_rig.oh.write(0)\r");         

        }

        public void StimAlign()
        {
           CvInvoke.NamedWindow(win1, NamedWindowType.Fullscreen);
           MCvScalar white = new Bgr(255, 255, 255).MCvScalar;
           img.SetTo(white); // set it to Neutral Gray color
           CvInvoke.Imshow(win1, img);
           CvInvoke.WaitKey(0);
           SizeF circle_size = new SizeF(40,40);
           Console.WriteLine("in Stim Align");
           while (true)
            {
                if (start_align)
                {                   
                    while (Math.Abs(projcenter_camcoords.X-camera_center.X) > 2 || Math.Abs(projcenter_camcoords.Y - camera_center.Y) > 2)
                    {
                        Ellipse centroid = new Ellipse(projector_center, circle_size, 0);
                        RotatedRect centrect = centroid.RotatedRect; // the rect that bounds the defined 
                        img.SetTo(white); // set it to Neutral Gray color
                        CvInvoke.Ellipse(img, centrect, new MCvScalar(0, 0, 0), -1);
                        CvInvoke.Imshow(win1, img); //Show the image
                        CvInvoke.WaitKey(1);
                        if (projcenter_camcoords.X < camera_center.X)
                            {
                                if (camera_center.X - projcenter_camcoords.X > 10)
                                {
                                    projector_center.Y -= 5;
                                }
                                else
                                {
                                    projector_center.Y -= 1;
                                }
                            }
                         if (projcenter_camcoords.Y < camera_center.Y)
                            {
                                if (camera_center.Y - projcenter_camcoords.Y > 10)
                                {
                                    projector_center.X += 5;
                                }
                                else
                                {
                                    projector_center.X += 1;
                                }
                            }
                         if (projcenter_camcoords.X > camera_center.X)
                            {
                                if (projcenter_camcoords.X - camera_center.X > 10)
                                {
                                    projector_center.Y += 5;
                                }
                                else
                                {
                                    projector_center.Y += 1;
                                }
                            }
                         if (projcenter_camcoords.Y > camera_center.Y)
                            {
                                if (projcenter_camcoords.Y - camera_center.Y > 10)
                                {
                                    projector_center.X -= 5;
                                }
                                else
                                {
                                    projector_center.X -= 1;
                                }
                            }
                        Console.WriteLine(projcenter_camcoords);
                        Console.WriteLine(projector_center);   
                        event1.Set();
                        event2.WaitOne();

                    }

                    alignment_complete = true;
                    CvInvoke.WaitKey(10);
                    event1.Set();
                    break;
                }
            }
          }
   

// THIS FUNCTION IS AN ABJECT MESS. No need to stop the experiment if the fish is in the middle. Who cares. 
// Also, alternate light and darkness trials. 
        public void EntrainLoom(int direction)
        {
            // this function will repeatedly deliver looming stimuli when the fish is in the middle of the tank to one or the other direction. 
            return;
        }

        private void ToggleCondition()
        {
            if (minefield_control)
            {
                experiment_phase++;
                experiment_timer.Reset();
                experiment_timer.Start();
                pre_escape = false;
            }
            if (darkness)
            {
                if (pre_escape)
                {
                    experiment_timer.Reset();
                    experiment_timer.Start();
                    experiment_phase++;
                }
                if (start_state_light)
                {
                    if (pre_escape)
                    {
                        pre_escape = false;
                    }
                    else
                    {
                        trialnumber++;
                    }
                }
                darkness = false;
                minefield = true;
                experiment_type = "_l";
            }
            if (minefield)
            {
                if (pre_escape)
                {
                    experiment_timer.Reset();
                    experiment_timer.Start();
                    experiment_phase++;
                }
                if (!start_state_light)
                {
                    if (pre_escape)
                    {
                        pre_escape = false;
                    }
                    else
                    {
                        trialnumber++;
                    }
                }
                darkness = true;
                minefield = false;
                experiment_type = "_d";
            }
        }
        
        public void StartStim()
        {
            int still_in_ROI = 1;
            if (!alignment_complete)
            {
                StimAlign();
                Calibration_Templates();
            }
            TankTemplate();
            int number_of_crossings = 0;
            centering_success = true;          
            
            experiment_timer.Start();
            while(true)
            {
               
                //    centering_success = OmrStimulus();
                Console.WriteLine(still_in_ROI.ToString());
                if (still_in_ROI == 1)
                {
                    if (mymotor.Position == 2)
                    {
                        mymotor.SetPosition(1, 1000);
                    }
                    Console.WriteLine("Radial Gradient On");
                    centering_success = RadialGradient(150);
                    Console.WriteLine("Centering Success Returned");
                  //  centering_success = OmrStimulus();
                    EmptyBuffer();
                }                
                if (centering_success) 
                {
               
                    Console.WriteLine("Experiment Duration (s)");
                    Console.WriteLine((experiment_timer.ElapsedMilliseconds / 1000).ToString());
                    if (minefield || minefield_control || memory || darkness)
                    {
                        still_in_ROI = GrayBr(); 
// fish must either leave inner arena or move next to a barrier to get a return from GrayBR.                                 
                        EmptyBuffer();
                    }
                    // !stil_in_ROI means fish has left inner arena. still_in_ROI = 0 means it is next to a barrier.                   
                    if (still_in_ROI == 1)
                    {
                        Console.WriteLine("outside ROI");
                        number_of_crossings++;
                        Console.WriteLine("Number Of Crossings");
                        Console.WriteLine(number_of_crossings.ToString());
                        //   continue;
                    }
// This gets called if the fish is in the center for more than 5 minutes. 
                    else if (still_in_ROI == 0)
                    {
                        Console.WriteLine("Timeout Trial " + trialnumber);
                        if (mymotor.Position == 2)
                        {
                            mymotor.SetPosition(1, 1000);
                        }
                        Thread.Sleep(-1);
                        experiment_complete = true;
                        break;
                    }
                    else {
                        if (stimtype == "loom")
                        {
                            // All you have to do to make all stim left or right is bias the generated barrier position to the left or right. input 'l' or 'r' to this function. 
                            // Also have to change this for real barrier condition
                            virtual_barrier_center = GenerateRandomBarrierPosition();
                            LoomingStimulus();
                        }
                        else if (stimtype == "darkflash")
                        {
                            LightsOut();
                        }
                        else if (stimtype == "tap" && !pre_escape)
                        {
                            bool tap_happened = Tap();
                            EmptyBuffer();
                            if (tap_happened)
                            {
                                ToggleCondition();
                            }
                        }
                        //else if (stimtype == "tap_dim")
                        //{
                        //    still_in_ROI = SlowDim("projector");
                        //    if (still_in_ROI)
                        //    {
                        //        Tap();
                        //    }
                        //}
                    }
                    if (pre_escape && experiment_timer.ElapsedMilliseconds > freerun_mins * 1000 * 60)
                    {
                        ToggleCondition();
                        if (pre_escape == false && number_of_crossings < crossing_thresh)
// if during the entire duration of the pre_escapes they didn't enter and leave the ROI, stop the experiment. 
                        { 
                            Console.WriteLine("Experiment Terminated--Crossing Threshold Not Reached");
                            experiment_complete = true;
                            if (mymotor.Position == 2)
                            {
                                mymotor.SetPosition(1, 1000);
                            }
                            Thread.Sleep(-1);
                            break;
                        }
                        continue;
                    }
                    else if ((experiment_timer.ElapsedMilliseconds > (escape_mins) * 1000 * 60) || (trialnumber > 10))
                    {
                        Console.WriteLine("Experiment Completed");
                        experiment_complete = true;
                        if (mymotor.Position == 2)
                        {
                            mymotor.SetPosition(1, 1000);
                        }
                        Thread.Sleep(-1);
                        break;
                    }
                }
                else
                {
                    bool nearwall = WallTap(walltrial);
                    if(nearwall) {walltrial++;}
                    //Console.WriteLine("Timeout Trial " + trialnumber);                    
                    //if (mymotor.Position == 2)
                    //{
                    //    mymotor.SetPosition(1, 1000);
                    //}
                    //experiment_complete = true; 
                    //break;
                }

            }

        }


        public void TankTemplate()
        {
            pyboard.WriteLine("escape_rig.ir.high()\r");
            MCvScalar black = new MCvScalar(0, 0, 0);
            MCvScalar bg_gray = new MCvScalar(150, 150, 150);
            img.SetTo(bg_gray);
            Ellipse alignment_circle = new Ellipse(new PointF(projector_center.X, projector_center.Y), new SizeF(templatewidth, templatewidth), 0);
            RotatedRect alignment_rect = alignment_circle.RotatedRect;
            CvInvoke.Ellipse(img, alignment_rect, black, 5);
            CvInvoke.Imshow(win1, img); 
            CvInvoke.WaitKey(0);
        }

        public void Calibration_Templates()
        {
            pyboard.WriteLine("escape_rig.ir.low()\r"); 
            MCvScalar black = new MCvScalar(0, 0, 0);
            MCvScalar backgroundcolor = new MCvScalar(255, 255, 255);
            img.SetTo(backgroundcolor);
            Console.WriteLine(projector_center);
            Ellipse calibration_circle = new Ellipse(new PointF(projector_center.X, projector_center.Y), new SizeF(templatewidth / 2, templatewidth / 2), 0);
            RotatedRect calibration_rect = calibration_circle.RotatedRect; // the rect that bounds the defined ellipse
            CvInvoke.Ellipse(img, calibration_rect, black, -1);
            CvInvoke.Imshow(win1, img); //Show the image
            CvInvoke.WaitKey(0); //THIS IS FOR THE CONTOUR OF THE TANKWIDTH
            pyboard.WriteLine("escape_rig.ir.high()\r");
            img.SetTo(backgroundcolor);
            Ellipse alignment_circle = new Ellipse(new PointF(projector_center.X, projector_center.Y), new SizeF(templatewidth, templatewidth), 0);
            RotatedRect alignment_rect = alignment_circle.RotatedRect; // the rect that bounds the defined ellipse
            CvInvoke.Ellipse(img, alignment_rect, black, 5);
            CvInvoke.Imshow(win1, img); // THIS IS FOR MODE CALCULATION.
            CvInvoke.WaitKey(0);
            float ROI_thresh_width = ((float)threshold_radius * ((float)templatewidth / (float)tankwidth));
            Console.WriteLine("Roi_thresh_width");
            Console.WriteLine(ROI_thresh_width);
            Ellipse ROI_thresh_circle = new Ellipse(new PointF(projector_center.X, projector_center.Y), new SizeF(ROI_thresh_width, ROI_thresh_width), 0);
            RotatedRect ROI_rect = ROI_thresh_circle.RotatedRect; // the rect that bounds the defined ellipse
            CvInvoke.Ellipse(img, ROI_rect, black, -1);
            CvInvoke.Imshow(win1, img); // THIS IS FOR MODE CALCULATION.
            CvInvoke.WaitKey(0);
        }

        private int GrayBr()
        {
            int inner_time_threshold = 300000;
            MCvScalar background_color = new MCvScalar(150, 150, 150);
            MCvScalar pixvals = new MCvScalar(0, 0, 0);
            if (darkness)
            {
                if(mymotor.Position == 1)
                {
                    mymotor.SetPosition(2, 1000);
                }
                img.SetTo(pixvals);  
                CvInvoke.Imshow(win1, img);
                CvInvoke.WaitKey(1);
            }
            else if (memory && !motor_closed)
            {
                for (int pixval = 150; pixval > 0; pixval--)
                {
                    pixvals = new MCvScalar(pixval, pixval, pixval);
                    img.SetTo(pixvals);
                    CvInvoke.Imshow(win1, img);
                    if (pixval == 150)
                    {
                        CvInvoke.WaitKey(10000);
// Allow 2 seconds of Gray
                    }
                    else
                    {
                        CvInvoke.WaitKey(50);
                    }
                }
                mymotor.SetPosition(2, 1000);
            }
            else
            {
                if (mymotor.Position == 2)
                {
                    mymotor.SetPosition(1, 1000);
                }
                img.SetTo(background_color);
                CvInvoke.Imshow(win1, img);
                CvInvoke.WaitKey(1); // Ten Second minimum timeout? if so implement here
            }
            EmptyBuffer();
            List<Point> fishlocations = new List<Point>();
            Stopwatch trial_clock = new Stopwatch();
            Stopwatch time_inner_arena = new Stopwatch();
            bool fish_near_barrier = false;
            trial_clock.Start();
            time_inner_arena.Start();
            while (true)
            {
                var camdata = pipe_buffer.Receive();
                if (time_inner_arena.ElapsedMilliseconds > inner_time_threshold)
                {
                    Console.WriteLine("Too Long In Center");
                    fish_near_barrier = false;
                    return 0;
                }
                if (trial_clock.ElapsedMilliseconds > 5) //record at 200 Hz
                {
                    fishlocations.Add(camdata.fishcoord);
                    for (int ind = 0; ind < barrier_position_list.Count; ind++)
                    {
                        barrier_center = barrier_position_list[ind];
                        barrier_radius = barrier_radius_list[ind];
                        if (CheckROI(camdata.fishcoord, "barrier"))
                        {
                            fish_near_barrier = true;
                            break;
                        }
                        if (CheckROI(camdata.fishcoord, "return"))
                        {
                            fish_near_barrier = false;
                            Console.WriteLine(camdata.fishcoord);
                            Console.WriteLine("Gray BR Exit");
                            return 1;
                        }
                    }
                    if (fish_near_barrier) { break; }
                    trial_clock.Restart();
                }
            }
            WriteCoordinateFile("/fishcoords_gray_trial", fishlocations);
            return 2;
        }
            

        

        private double VectorMag(PointF vec)
        {
            double vecmag = Math.Sqrt(vec.X * vec.X + vec.Y * vec.Y);
            return vecmag;
        }

        private double VectorMagFromBarrier(Point vec)
        {
            double vecmag = Math.Sqrt((vec.X - barrier_center.X) * (vec.X - barrier_center.X) + (vec.Y - barrier_center.Y) * (vec.Y - barrier_center.Y));
            return vecmag;
        }
        private double VectorMagFromCenter(Point vec)
        {
            double vecmag = Math.Sqrt((vec.X - tankcenter.X) * (vec.X - tankcenter.X) + (vec.Y - tankcenter.Y) * (vec.Y - tankcenter.Y));
            return vecmag;
        }
        private Point TransformPoint(Point pt)
        {
            Point newpoint = new Point();
            newpoint.X = (int)((pt.Y - camera_center.Y) * ((float)templatewidth / (float)tankwidth) + projector_center.X);
            newpoint.Y = (int)(projector_center.Y - (pt.X - camera_center.X) * ((float)templatewidth / (float)tankwidth));
            return newpoint;
        }

        private bool CheckROI(Point xypoint,string barrier_or_center)
        {
            int min_thresh_distance = 5;
            // Thresh distance was previously 15. 
            int thresh_distance = 18;
            int center_thresh = 200;
            int return_roi = 350;
            int wall_thresh = 440;
            double vmag = 0;
            if(barrier_or_center == "barrier")
            {
                vmag = VectorMagFromBarrier(xypoint);
                if (barrier_radius + min_thresh_distance < vmag && vmag < barrier_radius+thresh_distance)
                {
                    return true;
                }
                else
                {
                    return false;
                }

            }
            else if(barrier_or_center == "wall")
            {
                if (VectorMagFromCenter(xypoint) > wall_thresh)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else if(barrier_or_center == "center")
            {
                if (VectorMagFromCenter(xypoint) < center_thresh)
                {
                    return true;
                }
                else
                {
                    return false;
                }

            }
            else if(barrier_or_center == "return")
                if (VectorMagFromCenter(xypoint) > return_roi)
                {
                    return true;
                }
                else
                {
                    return false;
                }


            return true;              
        }

        // static void 

        private Point GenerateRandomBarrierPosition()
        {
            // HERE YOU WILL GENERATE ALL THE POINTS ON A CIRCLE. BEFORE CALLING LoomingStimulus() function in StartStims, generate a virtual barrier position that LoomingStim will transform into a projector coord. So your virtual barrier position will be in camera coordinates and will be the same distance away from 640,512 as your true barrier. so generate 10 random
            // points on a circle and take one of these as your virtual position per trial. 
            Point virtual_location = new Point();
            PointF barrier_vector = new PointF
            {
                X = Math.Abs(barrier_center.X - tankcenter.X),
                Y = Math.Abs(barrier_center.Y - tankcenter.Y)
            };
            int mag_diff = (int)VectorMag(barrier_vector);
            Random randomx = new Random();
            int virtual_x_coord = randomx.Next(-mag_diff, mag_diff) + tankcenter.X;
            int virtual_y_coord = (int)Math.Sqrt(Math.Pow(mag_diff,2) - Math.Pow(virtual_x_coord,2)) + tankcenter.Y;
            virtual_location.X = virtual_x_coord;
            virtual_location.Y = virtual_y_coord;
            return virtual_location;            
        }

        private bool SlowDim(string projector_or_oh) // PUT TEXT FILE OF POSITIONS HERE
        {
            int led_val = 100;
            int delay = 30;
            int dimcolor = 150;
         
            Stopwatch dim_timer = new Stopwatch();
            dim_timer.Start();
            if (projector_or_oh == "oh")
            {
                while (true)
                {
                    if (dim_timer.ElapsedMilliseconds >= delay)
                    {
                        pyboard.WriteLine("escape_rig.oh.write(" + led_val + ")\r");
                        led_val--;
                        dim_timer.Restart();
                    }
                    if (led_val == 0)
                    {
                        break;
                    }
                }
            }
            else if(projector_or_oh == "projector")
            {
                while (true)
                {
                    if (dim_timer.ElapsedMilliseconds >= delay)
                    {
                        MCvScalar br_color = new Bgr(dimcolor, dimcolor, dimcolor).MCvScalar;
                        img.SetTo(br_color);
                        CvInvoke.Imshow(win1, img);
                        CvInvoke.WaitKey(1);
                        dimcolor--;
                        dim_timer.Restart();
                    }
                    if (dimcolor == 15)
                    {
                        mymotor.SetPosition(2, 1000);
                        break;
                    }
                }

            }
// This has to be fixed to take from the pipe and use camdata.fishcoords 
            if (CheckROI(fish_center,"barrier"))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool Tap()
        {
            List<Point> coordlist = new List<Point>();
            List<byte[]> stim_pixels = new List<byte[]>();
            List<Tuple<uint, uint>> buffer_id = new List<Tuple<uint,uint>>();
            int framecount = 0;
            int proximity_frames = 100;
            string vidstring = "";
            string stimstring = "";
            string buffstring = "";
            vidstring = experiment_directory + "/tapmovie" + trialnumber.ToString("D2") + experiment_type + ".AVI";
            VideoWriter tapmovie = new VideoWriter(vidstring, 0, 500, new Size(80, 80), false);          
            while (true)
            {
                stim_in_progress = true;
                var camdata = pipe_buffer.Receive();
                coordlist.Add(camdata.fishcoord);
                stim_pixels.Add(camdata.pix_for_stim);
                tapmovie.Write(camdata.roi);
                Tuple<uint, uint> temptup = new Tuple<uint, uint>(camdata.jay, camdata.buffernumber);
                buffer_id.Add(temptup);
                framecount++;
                if (framecount == proximity_frames)
                {
                    if (CheckROI(camdata.fishcoord, "barrier"))
                    {
                        pyboard.WriteLine("escape_rig.taponce(240)\r");                        
                    }
                    else
                    {                       
                        Console.WriteLine("Left ROI");
                        stim_in_progress = false;
                        return false;
                    }
                }
                if (framecount == 2000)
                {
                    stim_in_progress = false;
                    break;
                }
            }
            tapmovie.Dispose();
            stimstring = experiment_directory + "/stimulus_trial" + trialnumber.ToString("D2") + experiment_type + ".txt";
            buffstring = experiment_directory + "/buffid_trial" + trialnumber.ToString("D2") + experiment_type + ".txt";
            WriteCoordinateFile("/tapresponse_trial", coordlist);
            using (StreamWriter sr = new StreamWriter(stimstring))
            {
                foreach (byte[] pix in stim_pixels)
                {
                    foreach (int pixel in pix)
                    {
                        sr.WriteLine(pixel.ToString());
                    }                    
                }
            }
            using (StreamWriter sr = new StreamWriter(buffstring))
            {
                foreach (Tuple<uint,uint> buff in buffer_id)
                {
                    sr.WriteLine("{0}\t{1}", buff.Item1, buff.Item2);
                }
            }
            return true;

        }

        private bool WallTap(int trialnum)
        {
            List<Point> coordlist = new List<Point>();
            List<byte[]> stim_pixels = new List<byte[]>();
            List<Tuple<uint, uint>> buffer_id = new List<Tuple<uint,uint>>();
            int framecount = 0;
            int proximity_frames = 100;
            string vidstring = "";
            string stimstring = "";
            string buffstring = "";
            int lastframe = 2000;
            vidstring = experiment_directory + "/wall_tap" + trialnum.ToString("D2") + experiment_type + ".AVI";
            VideoWriter tapmovie = new VideoWriter(vidstring, 0, 500, new Size(80, 80), false);
            while (true)
            {
                var camdata = pipe_buffer.Receive();
                if (framecount == 0)
                {
                    if (CheckROI(camdata.fishcoord, "center"))
                    {
                        return false;
                    }

                    if (!CheckROI(camdata.fishcoord, "wall"))
                    {
                        framecount = 0;
                        continue;
                    }
                }
                stim_in_progress = true;
                coordlist.Add(camdata.fishcoord);
                stim_pixels.Add(camdata.pix_for_stim);
                tapmovie.Write(camdata.roi);
                Tuple<uint, uint> temptup = new Tuple<uint, uint>(camdata.jay, camdata.buffernumber);
                buffer_id.Add(temptup);                
                framecount++;
                if (framecount == proximity_frames)
                {
                    if (!CheckROI(camdata.fishcoord, "wall"))
                    {
                        framecount = 0;
                        coordlist.Clear();
                        stim_in_progress = false;
                        stim_pixels.Clear();
                        buffer_id.Clear();
                        tapmovie.Dispose();
                        File.Delete(vidstring);
                        tapmovie = new VideoWriter(vidstring, 0, 500, new Size(80, 80), false);
                        continue;
                    }
                    else {
                        pyboard.WriteLine("escape_rig.taponce(240)\r");
                    }

                }
                if (framecount == lastframe)
                {
                    stim_in_progress = false;
                    break;
                  }
                }
            tapmovie.Dispose();
            stimstring = experiment_directory + "/wallstim_trial" + trialnumber.ToString("D2") + experiment_type + ".txt";
            buffstring = experiment_directory + "/wallbuff_trial" + trialnumber.ToString("D2") + experiment_type + ".txt";
            WriteCoordinateFile("/walltap_trial", coordlist);
            using (StreamWriter sr = new StreamWriter(stimstring))
            {
                foreach (byte[] pix in stim_pixels)
                {
                    foreach (int pixel in pix)
                    {
                        sr.WriteLine(pixel.ToString());
                    }                    
                }
            }
            using (StreamWriter sr = new StreamWriter(buffstring))
            {
                foreach (Tuple<uint,uint> buff in buffer_id)
                {
                    sr.WriteLine("{0}\t{1}", buff.Item1, buff.Item2);
                }
            }
            return true;

        }

        private void LightsOut()
        {            
            int framecount = 0;
            Stopwatch dark_timer = new Stopwatch();
            List<Point> fishcoords = new List<Point>();
            dark_timer.Start();
            pyboard.WriteLine("escape_rig.oh.write(150)\r");
            SlowDim("projector");
            while (true)
            {
                if (dark_timer.ElapsedMilliseconds > 2)
                {
                    fishcoords.Add(fish_center);
                    dark_timer.Restart();
                    framecount++;
                 //   Console.WriteLine(framecount);
                }
                if (framecount == 1500)   //record before darkflash for 3 seconds 
                {
               //     Console.WriteLine("Lights Out");
                    pyboard.WriteLine("escape_rig.oh.write(0)\r");
                }
                if (framecount == 6000)   //record for 10 seconds after the flash
                {
                    pyboard.WriteLine("escape_rig.oh.write(100)\r");
                    break;
                }
            }
            string darktrialstring = "";
            if(trialnumber < 10)
            {
                darktrialstring = experiment_directory + "/darkflash_trial0" + trialnumber.ToString() + experiment_type + ".txt";
            }
            else
            {
                darktrialstring = experiment_directory + "/darkflash_trial" + trialnumber.ToString() + experiment_type + ".txt";
            }

            using (StreamWriter darkcoords = new StreamWriter(darktrialstring))
            {
                foreach (Point fishpoint in fishcoords)
                {
                    darkcoords.WriteLine(fishpoint.ToString());
                }
            }
            while (true) // THIS ASSURES THAT FISH LEAVES THE ROI*2 SO THEY DONT RECEIVE REPETITIVE STIMULI.
            {
                CvInvoke.WaitKey(5);
                if(VectorMagFromCenter(fish_center) > (threshold_radius* threshold_multiplier))
                {
                    break;
                }
            }
          }

// LoomingStimulus needs to know which barrier to use as a reference. 
        private void LoomingStimulus()
        {
            string vidstring = "";
            // GENERATE LISTS OF TIMES FOR EACH POINT ACQUIRED. 
            vidstring = experiment_directory + "/loom_movie" + trialnumber.ToString("D2") + experiment_type + ".AVI";
            VideoWriter tapmovie = new VideoWriter(vidstring, 0, 500, new Size(80, 80), false);          
            List<Tuple<uint, uint>> buffer_id = new List<Tuple<uint,uint>>();
            MCvScalar gray = new Bgr(150, 150, 150).MCvScalar;
            SizeF looming_size = new SizeF(0, 0);
            List<Point> fishlocations = new List<Point>();
            List<PointF> stimlocations = new List<PointF>();
            List<float> times = new List<float>();
            PointF vecdiff = new PointF();
            double mag = 0;
            double pixelscale = 40;
            Random rnd = new Random();
            Point barrier_center_proj = new Point();
            bool isvirtual = false;
            if (rnd.Next(2) == 0)
            {
                barrier_center_proj = TransformPoint(barrier_center); // these are now in projector coords
                current_barrier_loc = barrier_center;
            }
            else
            {
                barrier_center_proj = TransformPoint(virtual_barrier_center); // these are now in projector coords
                current_barrier_loc = virtual_barrier_center;
                isvirtual = true;
            }
            string barrier_string = "";
            barrier_string = experiment_directory + "/barriervarbs_trial0" + trialnumber.ToString("D2") + experiment_type + ".txt";
            using (StreamWriter barrier_varbs = new StreamWriter(barrier_string))
            {
                barrier_varbs.WriteLine(barrier_center_proj.ToString());
                barrier_varbs.WriteLine("Is Virtual?");
                barrier_varbs.WriteLine(isvirtual.ToString());
            }
            Stopwatch stim_timer = new Stopwatch();
            // BUILD IN A DELAY BEFORE THE BEGINNING OF LOOM, BUT RECORD FISHLOCATION DURING THIS DELAY:
            int num_stim_displayed = 0;
            stim_timer.Start();
            while (true)
            {
                stim_in_progress = true;
                var camdata = pipe_buffer.Receive();
                fishlocations.Add(camdata.fishcoord);
                tapmovie.Write(camdata.roi);
                Tuple<uint, uint> temptup = new Tuple<uint, uint>(camdata.jay, camdata.buffernumber);
                buffer_id.Add(temptup);
                Point fish_center_proj = TransformPoint(camdata.fishcoord);
                if (stim_timer.ElapsedMilliseconds >= 50) // Each of these should be 50 ms from start of trial. 
// 50 ms is required so projector refreshes evenly (i.e. every 3 projector refreshes gets a new frame). 
                {
                    looming_diam = num_stim_displayed * growth_rate;
                    looming_size.Height = looming_diam;
                    looming_size.Width = looming_diam;
                    if (looming_diam > looming_size_thresh)
                    {
                        Console.WriteLine("Looming Complete");
                        break;
                    }
                    img.SetTo(gray);
                    // TRANSFORM BOTH POINTS FIRST. THEN TAKE THE DIFF. 
                    vecdiff.X = fish_center_proj.X - barrier_center_proj.X;
                    vecdiff.Y = fish_center_proj.Y - barrier_center_proj.Y;
                    mag = VectorMag(vecdiff);
                    stimcenter.X = (float)(fish_center_proj.X + (vecdiff.X / mag) * pixelscale);
                    stimcenter.Y = (float)(fish_center_proj.Y + (vecdiff.Y / mag) * pixelscale);
                    Ellipse myellipse = new Ellipse(stimcenter, looming_size, 0);
                    RotatedRect rect = myellipse.RotatedRect; // the rect that bounds the defined ellipseRotatedRect.
                    CvInvoke.Ellipse(img, rect, new MCvScalar(0, 0, 0), -1);
                    CvInvoke.Imshow(win1, img); //Show the image
                                                // CvInvoke.WaitKey(50)                    ; // instead of this being a waitkey, use an "if 50 ms has passed" statement, but write to fish and stim locations every ms. 
                    stimlocations.Add(stimcenter);
                    num_stim_displayed++;
                    CvInvoke.WaitKey(1);
                    stim_timer.Restart();
                }
            }
            looming_diam = 1;
            string stimstring = "";
            string buffstring = "";
            stimstring = experiment_directory + "/looming_stimcoords_trial" + trialnumber.ToString("D2") + experiment_type + ".txt";
            buffstring = experiment_directory + "/looming_buffID_trial" + trialnumber.ToString("D2") + experiment_type + ".txt";
            WriteCoordinateFile("/looming_fishcoords_trial", fishlocations);
            using (StreamWriter sr2 = new StreamWriter(stimstring))
            {
                foreach (PointF stimpoint in stimlocations)
                {
                    sr2.WriteLine(stimpoint.ToString());
                }
            }
            using (StreamWriter sr = new StreamWriter(buffstring))
            {
                foreach (Tuple<uint,uint> buff in buffer_id)
                {
                    sr.WriteLine("{0}\t{1}", buff.Item1, buff.Item2);
                }
            }
            tapmovie.Dispose();
            return;
        }


        private bool RadialGradient(int maxluminance)
        {
            List<Point> fishlocations = new List<Point>();
            MCvScalar backgroundcolor = new Bgr(255, 255, 255).MCvScalar;
            MCvScalar black = new Bgr(0, 0, 0).MCvScalar;
            img.SetTo(black);
            // generate a radial gradient stimulus centered on projcenter_x. largestcircle = templatewidth. 
            float increment = (float)templatewidth / (float)maxluminance;
            int outer_time_threshold = 300000;
            Mat radial_grad = new Mat(2000, 2000, DepthType.Cv8U, 3);
            img.SetTo(new MCvScalar(maxluminance, maxluminance, maxluminance));
            CvInvoke.Imshow(win1, img);
            CvInvoke.WaitKey(1);
            for (int pixval = 0; pixval <= maxluminance; pixval += 3)
            {
                MCvScalar color = new MCvScalar(pixval, pixval, pixval);
                float radius = (maxluminance - pixval) * increment;
                Ellipse grad_circle = new Ellipse(projector_center, new SizeF(radius, radius), 0);
                RotatedRect grad_rect = grad_circle.RotatedRect;
                CvInvoke.Ellipse(radial_grad, grad_rect, color, -1);
            }  
            CvInvoke.Imshow(win1, radial_grad);
            CvInvoke.WaitKey(1);   
            Stopwatch phototaxis_clock = new Stopwatch();
            Stopwatch trial_clock = new Stopwatch();
            bool phototaxis_successful = false;
            phototaxis_clock.Start();
            trial_clock.Start();
            while (true)
            {               
                var camdata = pipe_buffer.Receive();
                if (phototaxis_clock.ElapsedMilliseconds > outer_time_threshold)
                {
                    phototaxis_successful = false;
                    break;
                }
                if (trial_clock.ElapsedMilliseconds > 5) //record at 200 Hz
                {                    
                    fishlocations.Add(camdata.fishcoord);
                    if (CheckROI(camdata.fishcoord,"center"))
                    {
                        phototaxis_successful = true;
                        break;
                    }
                    trial_clock.Restart();
                }
            }
            WriteCoordinateFile("/fishcoords_ptax_trial", fishlocations);
            return phototaxis_successful;
        }

        private void EmptyBuffer()
        {
            var camdata = pipe_buffer.Receive();
            while (pipe_buffer.TryReceive(out camdata)) {}
        }

        private void WriteCoordinateFile(string file_id, List<Point> coordinate_list)
        {
            string file_string = experiment_directory + file_id + trialnumber.ToString("D2") + experiment_type + ".txt";
            using (StreamWriter sr = new StreamWriter(file_string))
            {
                foreach (Point coord in coordinate_list)
                {
                    sr.WriteLine(coord.ToString());
                }
            }
        }

        private bool OmrStimulus()
        {
            Console.WriteLine("OMR ON");
            List<Point> fishlocations = new List<Point>();
            MCvScalar black = new MCvScalar(0,0,0);
            MCvScalar white = new MCvScalar(150, 150, 150);
            MCvScalar outer_and_odd_color = black;
            MCvScalar inner_and_even_color = white;
            MCvScalar backgroundcolor = white;
            MCvScalar circle_color = black;
            int number_of_circles = 12;
            int interval_width = 80;
            int diam_outer_circle = 960;
            int center_x = projector_center.X;
            int center_y = projector_center.Y;
            bool br_is_white = true;
            img.SetTo(backgroundcolor);
            CvInvoke.Imshow(win1, img);
            CvInvoke.WaitKey(100);
            Stopwatch omr_clock = new Stopwatch();
            bool omr_successful = false;
            omr_clock.Start();
            int circle_width = diam_outer_circle;
            while (true)
            {
                if (omr_clock.ElapsedMilliseconds > 300000)
                {
                    omr_successful = false;
                    break;       
                }
                var camdata = pipe_buffer.Receive();
                fishlocations.Add(camdata.fishcoord);
                if (CheckROI(camdata.fishcoord,"center"))
                {
                    img.SetTo(white);
                    CvInvoke.Imshow(win1, img);
                    CvInvoke.WaitKey(1);
                    omr_successful = true;
                    break;
                }
                img.SetTo(backgroundcolor);
                for (int circle_index = 0; circle_index < number_of_circles; circle_index++)
                {
                    int current_width = circle_width - (interval_width * circle_index);
                    Ellipse omr_circle = new Ellipse(new PointF(center_x, center_y), new SizeF(current_width, current_width), 0);
                    RotatedRect encapsulating_rect = omr_circle.RotatedRect; // the rect that bounds the defined ellipse
                    if (circle_index % 2 == 0)
                    {
                        circle_color = outer_and_odd_color;
                    }
                    else
                    {
                        circle_color = inner_and_even_color;
                    }
              //      img.SetTo(backgroundcolor);
                    CvInvoke.Ellipse(img, encapsulating_rect, circle_color, -1);
                //    CvInvoke.Imshow(win1, img);
//                    CvInvoke.WaitKey(1);
                }
                CvInvoke.Imshow(win1, img);
                CvInvoke.WaitKey(5);
                circle_width--;
                if (circle_width < diam_outer_circle - interval_width)
                {
                    circle_width = diam_outer_circle;
                    if (br_is_white)
                    {
                        outer_and_odd_color = white;
                        inner_and_even_color = black;
                        br_is_white = false;
                    }
                    else {
                        outer_and_odd_color = black;
                        inner_and_even_color = white;
                        br_is_white = true;
                    }
                }
            }
            string omrstring = "";
            if(trialnumber < 10)
            {
                omrstring = experiment_directory + "/fishcoords_omr_trial0" + trialnumber.ToString() + experiment_type + ".txt";
            }
            else
            {
                omrstring = experiment_directory + "/fishcoords_omr_trial" + trialnumber.ToString() + experiment_type + ".txt";
            }
            using (StreamWriter sr = new StreamWriter(omrstring))
            {
                foreach (Point fishpoint in fishlocations)
                {
                    sr.WriteLine(fishpoint.ToString());
                }
            }
            return omr_successful;
        }                  

    }
}
