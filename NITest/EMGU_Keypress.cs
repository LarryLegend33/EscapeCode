using System;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace EMGU_Keypress
{
    class emguKeyPress
    {
        // public char key;
        public int key;
        String keywindow;
        Mat img;

        public emguKeyPress()
        {
            keywindow = "KeyPress Window";
          
            img = new Mat(250, 250, DepthType.Cv8U, 3);
            img.SetTo(new Bgr(0,0,0).MCvScalar);
            CvInvoke.NamedWindow(keywindow);
            CvInvoke.Imshow(keywindow,img);
          
        }

        public void CheckKey()
        {

            while (true)
            {
//                key = (char)CvInvoke.WaitKey(1000);
                key = CvInvoke.WaitKey(1000);
                Console.WriteLine(key);
            }
        }
    }
}
