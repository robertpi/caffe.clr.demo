#r "System.Drawing.dll"
#r "System.Windows.Forms.dll"
 
open System
open System.Drawing
open System.Windows.Forms
 
let rand = Random()
let width, height = 512, 512
 
let createCircles () =
   let toCircles k length =
      Seq.unfold (fun distance ->
         let d = length/32 + rand.Next(length/k)  
         if (distance + d) < length then Some((d,distance), distance + d)
         else None
      ) 0
      |> Seq.toArray
   let toRect k length w h =
      let lines = [        
         w/2,h/2, -1, 0, 0      // b   r to l        
         -w/2, h/2, 0, -1, 90   // l   b to t
         -w/2, -h/2, 1, 0, 180  // t   l to r
         w/2,-h/2, 0, 1, 270    // r   t to b
      ]
      [for (x,y,dx,dy,a) in lines do
         let rs = toCircles k length
         yield!
            [for (d,k) in rs ->
               let n = k + d/2
               d, x + n*dx, y + n*dy, a]
      ]
   let rects = [75, 5; 53, 4; 35, 4; 22, 3; 13, 2] |> List.rev
   [for pc,k in rects do
      let w = width * pc / 100
      let h = height * pc / 100
      let circles = toRect k w w h
      for (d,x,y,a) in circles do      
         let x = width/2 + x
         let y = height/2 + y
         yield (x,y,d,a)
   ]
 
let draw circles =
   let image = new Bitmap(width, height)
   use graphics = Graphics.FromImage(image)
   graphics.SmoothingMode <- System.Drawing.Drawing2D.SmoothingMode.AntiAlias
   let color = Color.FromArgb(255,234,236,232)
   let brush = new SolidBrush(color)
   graphics.FillRectangle(brush, 0, 0, width, height)
   (*
   let rects = [75; 53; 35; 22; 13] |> List.rev
   rects |> Seq.iter (fun pc ->
      let w = width * pc / 100
      let h = height * pc / 100      
      graphics.DrawRectangle(Pens.Black, width/2-w/2, height/2-h/2, w, h)            
   )
   *)
   circles |> List.fold (fun xs circle ->
      let x,y,d,a = circle
      let isTouching =
         xs |> List.exists (fun (x1,y1,d1,_) ->
               let k = ((x1-x)*(x1-x))+((y1-y)*(y1-y))
               let n = sqrt(float k)
               n < ((float (d1-1)/2.0) + (float (d-1)/2.0))
         )
      let x, y = x - d/2, y - d/2
      let color = Color.FromArgb(255,247,171,27)
      let brush = new SolidBrush(color)
      if isTouching        
      then graphics.FillPie(brush, x, y, d, d, a, 180)
      else graphics.FillEllipse(brush, x, y, d, d)
      circle::xs
   ) []
   |> ignore
   image
 
(*
let show () =
   let image = draw ()
   let form = new Form (Text="Circles", Width=320+16, Height=320+36)  
   let picture = new PictureBox(Dock=DockStyle.Fill, Image=image)
   do  form.Controls.Add(picture)
   form.ShowDialog() |> ignore
 
show()
*)
 
#r @"Gif.Components.dll"
 
open Gif.Components
 
let circles = createCircles ()
let encoder = AnimatedGifEncoder()
 
if encoder.Start(@"c:\temp\Loewensberg step-by-step.gif") then
   encoder.SetFrameRate(10.0f)
   encoder.SetRepeat 0
   for i = 1 to circles.Length do
      let circles = circles |> Seq.take i |> Seq.toList
      encoder.AddFrame(draw circles) |> ignore
   for i = 1 to 10 do
      encoder.AddFrame(draw circles) |> ignore  
   encoder.Finish() |> ignore