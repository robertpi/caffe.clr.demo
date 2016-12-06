open System
open System.IO
open System.Diagnostics
open System.Drawing
open System.Drawing.Imaging
open System.Runtime.InteropServices
open Caffe.Clr

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

// iterates for over a given matrix, each pass highlights what is recongized
let makeStep (net: Net) (inputBlob: Blob) width height (data: float32[]) (layer: string) (outputBlob: Blob) =
    time "makeStep" (fun () -> 

        // first add the data to the blob
        inputBlob.SetData(data)

        for i in 1 .. 10 do
            // first get the blobs data
            let inputData = inputBlob.GetData()

            // adding a random jitter helps the net find aspects of the image
            let xJitter, yJitter = rnd.Next(-32, 32), rnd.Next(-32, 32)
            let inputData = PseudoMatrices.splitRollCombine inputData width height xJitter yJitter
            inputBlob.SetData(inputData)

            // forward to a given layer, set that layers target (the diff)
            // the propergate the data back the inputBlob
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

let makeDreamForLayer layer  imgFile meanFile (net: Net) (inputBlob: Blob) =
    // get a reference to the bolb of the output layer
    let outputBlobOpt = net.BlobByName(layer)

    match outputBlobOpt with
    | Some outputBlob ->

        //load the image into an array formatted for use with caffe
        let size, mean, allChannels = loadImageIntoArray imgFile meanFile

        // zoom over different levels of the image to help the net find different element
        for zoomFactor in 4 .. -1 .. 1  do

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
                    let result = makeStep net inputBlob edgeLength edgeLength clippedMatrix layer outputBlob
                    let unclippedResult = PseudoMatrices.splitUnclipSquareCombine zoomWidth zoomHeight result subMatrix
                    unclippedResult

                // run the highlight over each zoomed image piece
                let treadedParts =
                    Array.map processMatrix subMatrices 

                // put the image back together and save
                PseudoMatrices.unzoom zoomFactor size.Width size.Height treadedParts allChannels
                let imageToSave = ArrayHelpers.arrayAdd mean allChannels
                DotNetImaging.saveImageDotNet imageToSave (getOutputFileName imgFile layer "" zoomFactor) size)

    | None -> printfn "Layer %s has no blob" layer


[<EntryPoint>]
let main argv = 
    // unpack arguments related to the model
    let modelFile   = argv.[0]
    let trainedFile = argv.[1]
    let meanFile    = argv.[2]

    // unpack the argument for the files to be tested
    let imgFile = argv.[3]

    // unpack the layer argument
    let layer = 
        if argv.Length > 4 then argv.[4]
        else "inception_4c/output"

// uncomment to run on the GPU, can make quite a difference!
//    Caffe.SetMode Brew.GPU
//    let deviceId = Caffe.FindDevice()
//    Caffe.SetDevice deviceId

    time "main" (fun () ->

        // load the net's model and import the train data
        let net = loadModel modelFile trainedFile

        // get the input blob where we'll put the image data
        let inputBlob = net.InputBlobs |> Seq.head

        if layer = "*" then
            for layer in net.LayerNames |> Seq.filter(fun layer -> layer <> "data")  do
                makeDreamForLayer layer imgFile meanFile net inputBlob 
        else
                makeDreamForLayer layer imgFile meanFile net inputBlob)


    0 // return an integer exit code
