open System
open System.Collections.Generic
open System.IO
open System.Diagnostics
open System.Drawing
open System.Drawing.Imaging
open System.Runtime.InteropServices
open Caffe.Clr
open SimpleVideoEncoder

// three color images, so three channels in our matrix
let numChannels = 3

// initalize the random number generators
let rnd = new Random()

// helper function to time execution
let time name func =
    let clock = Stopwatch.StartNew()
    let result = func()
    printfn "%s time taken: %O" name clock.Elapsed
    result

let getOutputFileName filepath (layer: string) extraPart i =
    let directory = Path.GetDirectoryName(filepath)
    let filename = Path.GetFileNameWithoutExtension(filepath)
    let ext = Path.GetExtension(filepath)
    let layerSafe = layer.Replace("/", "-")
    Path.Combine(directory, sprintf "%s_%s_%s%i%s" filename layerSafe extraPart i ext)

let loadModel modelFile trainedFile =
    // load the network and training data
    let net = new Net(modelFile, Phase.Test)
    net.CopyTrainedLayersFrom(trainedFile)

    // check the network has the right number of inputs/outputs
    assert (net.InputBlobs.Count = 1)
    assert (net.OutputBlobs.Count = 1)

    net 

let createBitmap (data: float32[]) file (size: Size) =
    let bitmap = new Bitmap(size.Width, size.Height)

    let b, g, r = PseudoMatrices.splitChannels data

    let rgb = Seq.zip3 r g b 

    rgb |> Seq.iteri (fun i (r, g, b)  ->
        let x = i % size.Width
        let y = i / size.Width
        let intMax255 x =
            max (min (int x) 255) 0
        let p = Color.FromArgb(intMax255 r, intMax255 g, intMax255 b)
        bitmap.SetPixel(x, y, p))

    bitmap

let drawTextAndBox (g: Graphics) text fontSize left top =
    let font = new Font("Calibri", fontSize)
    let size = g.MeasureString(text, font)
    g.FillRectangle(new SolidBrush(Color.FromArgb(128, 255, 255, 255)), left - 12, top - 12, int size.Width + 24, int size.Height + 18)
    g.DrawString(text, font, Brushes.Black, float32 left, float32 top)

// iterates for over a given matrix, each pass highlights what is recongized
let makeStep (net: Net) (inputBlob: Blob) width height (data: float32[]) (layer: string) text (outputBlob: Blob) mean imgFile (video: VideoEncoder) =
    time "makeStep" (fun () -> 

        // first add the data to the blob
        inputBlob.SetData(data)

        let imageStack = new Stack<Bitmap>()

        for i in 1 .. 150 do
            // first get the blobs data
            let inputData = inputBlob.GetData()

            // adding a random jitter helps the net find aspects of the image
            let xJitter, yJitter = rnd.Next(-32, 32), rnd.Next(-32, 32)
            let inputData = PseudoMatrices.splitRollCombine inputData width height xJitter yJitter
            inputBlob.SetData(inputData)

            // forward to a given layer, set that layers target (the diff)
            // the propagate the data back the inputBlob
            net.ForwardTo(layer) |> ignore
            outputBlob.SetDiff(outputBlob.GetData())
            net.BackwardFrom(layer)

            // the inputBlob's diff now contains what the net has recognized
            let inputData = inputBlob.GetData()
            let inputDiff = inputBlob.GetDiff()

            // apply the recognized data to the image data
            let absMean = inputDiff |> Seq.map Math.Abs |> Seq.average
            let inputData' =
                Seq.zip inputData inputDiff 
                |> Seq.map(fun (data, diff) -> data + (1.5f / absMean * diff))
                |> Seq.toArray

            // undo the roll
            let inputData'' = PseudoMatrices.splitRollCombine inputData' width height -xJitter -yJitter

            // set the data read for the next pass
            inputBlob.SetData(inputData'')

            if i % 5 = 0 then
                let imageToSave = ArrayHelpers.arrayAdd mean inputData''
                let bitmap = createBitmap imageToSave (getOutputFileName imgFile layer "" i) (new Size(width, height))
                use g = Graphics.FromImage(bitmap)
                drawTextAndBox g text 22.f 24 24
                drawTextAndBox g (sprintf "layer: %s" layer) 16.f 24 (height - 48)
                imageStack.Push(bitmap.Clone() :?> Bitmap)
                video.AddFrame(bitmap)

        let rec loop() =
            if imageStack.Count > 0 then
                let bitmap = imageStack.Pop()
                video.AddFrame(bitmap)
                loop()

        loop()

        // at the end of the interatiosn retun the image data
        inputBlob.GetData())

let loadImageIntoArray imgFile meanFile =
    // load the image into standard .NET bitmap object
    let bitmap = Image.FromFile(imgFile) :?> Bitmap

    // resize the bitmap so the long edge is no larger that 1000 pixels
    let bitmapResized = DotNetImaging.resizeBitmap bitmap 1000.

    // convert image into an array in the format used by caffe
    let allChannels = DotNetImaging.formatBitmapAsBgrChannels bitmapResized

    // get the size of the image
    let size = bitmapResized.Size

    // load the mean file from the test data
    let mean = BlobHelpers.loadMean meanFile numChannels size.Width size.Height

    // subtract the mean value from the channel values
    ArrayHelpers.arraySubInPlace mean allChannels

    // return the size, mean and channel data
    size, mean, allChannels

let clipActionUnclip subMatrix zoomWidth zoomHeight action =
    let subMatrix' = PseudoMatrices.splitClipSquareCombine zoomWidth zoomHeight subMatrix
    let result = action subMatrix'
    let unclippedResult = PseudoMatrices.splitUnclipSquareCombine zoomWidth zoomHeight result subMatrix
    unclippedResult

let makeDreamForLayer layer text imgFile meanFile (net: Net) (inputBlob: Blob) (video: VideoEncoder) =
    // get a reference to the bolb of the output layer
    let outputBlobOpt = net.BlobByName(layer)

    match outputBlobOpt with
    | Some outputBlob ->

        //load the image into an array formatted for use with caffe
        let size, mean, allChannels = loadImageIntoArray imgFile meanFile

        // zoom over different levels of the image to help the net find different element
        for zoomFactor in 1 .. -1 .. 1  do

            time (sprintf "zoom %i" zoomFactor) (fun () ->

                // perform the actual zooming
                let zoomWidth, zoomHeight, subMatrices = PseudoMatrices.splitZoomCombine zoomFactor size.Width size.Height allChannels

                let edgeLength =
                    if zoomWidth > zoomHeight then zoomHeight
                    else zoomWidth

                // reshape the net to the target image size
                inputBlob.Reshape([|1; numChannels; edgeLength; edgeLength|])
                net.Reshape()

                let processMatrix subMatrix =
                    let clippedMatrix = PseudoMatrices.splitClipSquareCombine zoomWidth zoomHeight subMatrix
                    let result = makeStep net inputBlob edgeLength edgeLength clippedMatrix layer text outputBlob mean imgFile video
                    let unclippedResult = PseudoMatrices.splitUnclipSquareCombine zoomWidth zoomHeight result subMatrix
                    unclippedResult

                // run the highlight over each zoomed image piece
                let treadedParts =
                    Array.map processMatrix subMatrices 

                // put the image back together and save
                PseudoMatrices.unzoom zoomFactor size.Width size.Height treadedParts allChannels
                let imageToSave = ArrayHelpers.arrayAdd mean allChannels
                DotNetImaging.saveImageDotNet imageToSave (getOutputFileName imgFile layer "" zoomFactor) size
            )

    | None -> printfn "Layer %s has no blob" layer


[<EntryPoint>]
let main argv = 
    // unpack arguments related to the model
    let modelFile   = argv.[0]
    let trainedFile = argv.[1]
    let meanFile    = argv.[2]

    // unpack the argument for the files to be tested
    let imgFile = argv.[3]

    let layers = 
        [ "conv1/7x7_s2", "This is Deep Dream"
          "conv2/3x3", "images generated by ..."
          "inception_3a/1x1", "neural networks."
          "inception_3a/output", "Each sequence is ..."
          "inception_3b/output", "generated by ..."
          "inception_4a/output", "a different layer."
          "inception_4b/output", "Come see how ..."
          "inception_4c/output", "at F# exchange."
          "inception_5b/output", "London, 6th-7th April" ]

// uncomment to run on the GPU, can make quite a difference!
    Caffe.SetMode Brew.GPU
    let deviceId = Caffe.FindDevice()
    Caffe.SetDevice deviceId

    time "main" (fun () ->

        // load the net's model and import the train data
        let net = loadModel modelFile trainedFile

        // get the input blob where we'll put the image data
        let inputBlob = net.InputBlobs |> Seq.head

        use video = new VideoEncoder(Path.ChangeExtension(imgFile, "mp4"), 664, 664)

        for (layer, text) in layers do
            makeDreamForLayer layer text imgFile meanFile net inputBlob video)


    0 // return an integer exit code
