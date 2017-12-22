

class DarkFlash
    { 
        Stopwatch dark_timer = new Stopwatch();
        List<Point> fishcoords = new List<Point>();
        public Point barrier_center, fish_center,projcenter_camcoords,current_barrier_loc;
        public volatile int trialnumber, tankwidth, delay, templatewidth, stim_radius;
        public volatile bool start_align, alignment_complete;
        SerialPort pyboard = new SerialPort("COM3",115200);

        public DarkFlash()
        {
            alignment_complete = true;
            tankwidth = 980;
            stim_radius = 500;
            barrier_center.X = 0;
            barrier_center.Y = 0;
            fish_center.X = 0;
            fish_center.Y = 0;
            pyboard.Open();
            pyboard.WriteLine("import escape_rig\r");
            pyboard.WriteLine("escape_rig.ir.high()\r");
            pyboard.WriteLine("escape_rig.oh.write(150)\r");
            trialnumber = 1;
        }

        public void StartStim()
        {
            while (true)
            {
                if (Math.Abs(fish_center.X - 640) < stim_radius && Math.Abs(fish_center.Y - 512) < stim_radius)
                {
                    LightsOut();
                    trialnumber++;                           
                    Thread.Sleep(10000); // 30 sec delay between trials
                }
            }       
        }



        private void LightsOut()
        {            
            int framecount = 0;
            Stopwatch dark_timer = new Stopwatch();
            dark_timer.Start();
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
                    pyboard.WriteLine("escape_rig.oh.write(150)\r");
                    break;
                }
            }

            using (StreamWriter darkcoords = new StreamWriter("C:/Users/Deadpool/Desktop/darkflash_trial" + trialnumber.ToString() + ".txt"))
            {
                foreach (Point fishpoint in fishcoords)
                {
                    darkcoords.WriteLine(fishpoint.ToString());
                }
            }

          }

            


    }

    class DimAndStim
    {
        public Point barrier_center, fish_center,projcenter_camcoords,current_barrier_loc;
        SerialPort py_board;
        Stopwatch dim_timer = new Stopwatch();
        Stopwatch tap_timer = new Stopwatch();
        public volatile int trialnumber, tankwidth, delay, templatewidth, stim_radius;
        public volatile bool start_align, alignment_complete, dimornot;
        SerialPort pyboard = new SerialPort("COM3",115200);
        

        public DimAndStim(bool dim_or_not)
        {
            
            alignment_complete = true;
            barrier_center.X = 0;
            barrier_center.Y = 0;
            fish_center.X = 0;
            fish_center.Y = 0;
            delay = 50;
            trialnumber = 1;
            dimornot = dim_or_not;
            tankwidth = 980;
            stim_radius = 400;
            pyboard.Open();
            pyboard.WriteLine("import escape_rig\r");
            pyboard.WriteLine("escape_rig.ir.high()\r");
            //            pyboard.WriteLine("escape_rig.tap.write(0)\r");
            pyboard.WriteLine("escape_rig.tap.low()\r");
            pyboard.WriteLine("escape_rig.oh.write(150)\r");


        }
        // SO YOU CANNOT TOSS A PYBOARD OBJECT OVER THREADS. 
        public void StartStim()
        {
        
            Console.WriteLine("StartStim Initiated");
            while (true)
            {
                if (Math.Abs(fish_center.X - 640) < stim_radius && Math.Abs(fish_center.Y - 512) < stim_radius)
                {
                    if (dimornot)
                    {
                        SlowDim();
                    }
                    Tap();            
                }
                trialnumber++;
                Thread.Sleep(5000);

            }
          
        }


        private void SlowDim() // PUT TEXT FILE OF POSITIONS HERE
        {
            int led_val = 150;
            dim_timer.Start();
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
         
        private void Tap()
        {
            List<Point> coordlist = new List<Point>();
            int framecount = 0;
         //   pyboard.WriteLine("test.tap.write(250)\r");
            pyboard.WriteLine("escape_rig.taponce()\r");
            tap_timer.Start();
            while (true)
            {
//               Console.WriteLine(framecount);
               if(tap_timer.ElapsedMilliseconds > 2)
               {
                  //  Console.WriteLine("past 2");
                    coordlist.Add(fish_center);
                    framecount++;
                    tap_timer.Restart();
                }
               if(framecount == 5000)
               {
                    break;
               }              
            }          
            pyboard.WriteLine("escape_rig.oh.write(150)\r");
            using (StreamWriter sr = new StreamWriter("C:/Users/Deadpool/Desktop/tapresponse_trial" + trialnumber.ToString() + ".txt"))
            {
                foreach (Point fishpoint in coordlist)
                {
                    sr.WriteLine(fishpoint.ToString());
                }
            }
            trialnumber++;
        }
        
      

    }



