#r "System.Drawing.dll"
#r "System.Windows.Forms.dll"
#r @"Gif.Components.dll"

open System.Drawing
open Gif.Components
 
let encoder = AnimatedGifEncoder()
 
if encoder.Start(@"C:\code\Caffe.clr.demo\data\maxcat.gif") then
   encoder.SetFrameRate(20.0f)
   encoder.SetRepeat 0
   for i in 1 .. 300 do
      encoder.AddFrame(Image.FromFile (sprintf @"C:\code\Caffe.clr.demo\data\maxcat_inception_4c-output_%i.jpg" i)) |> ignore
      if i % 10 = 0 then
        printfn "doing %i ..." i
   encoder.Finish() |> ignore
