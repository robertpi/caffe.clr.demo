namespace Caffe.Clr.Examples
module Classification =

    open System.IO
    open System.Drawing
    open System.Drawing.Imaging
    open System.Runtime.InteropServices
    open Caffe.Clr

    let loadImageIntoBlob file (size: Size) meanFile (inputBlob: Blob) numChannels =
        // load the image into standard .NET bitmap object
        let bitmap = Image.FromFile(file) :?> Bitmap

        // resize the image to the input blob
        let bitmap = new Bitmap(bitmap, size)

        // convert image into an array in the format used by caffe
        let allChannels = DotNetImaging.formatBitmapAsBgrChannels bitmap

        // load the mean file from the test data
        let mean = BlobHelpers.loadMean meanFile numChannels size.Width size.Height 

        // apply the mean file to the channels
        ArrayHelpers.arraySubInPlace mean allChannels

        // set the channel data on the input blob
        inputBlob.SetData(allChannels)

    let loadModel modelFile trainedFile =
        // load the network and training data
        let net = new Net(modelFile, Phase.Test)
        net.CopyTrainedLayersFrom(trainedFile)

        // check the network has the right number of inputs/outputs
        assert (net.InputBlobs.Count = 1)
        assert (net.OutputBlobs.Count = 1)

        net 

    let printOutput (net: Net) labelFile =
        // grab a reference to the output blob
        let output = net.OutputBlobs |> Seq.head

        // read the labels file
        let labels = File.ReadAllLines(labelFile)

        // check the network has the same number of outputs as the labels file
        let resultChannels = output.Shape(Shape.Channels)
        //printfn "labels.Length = %i resultChannels = %i" labels.Length resultChannels
        assert (labels.Length = resultChannels)

        // get the output data
        let resultData = output.GetData()

        // match the results to labels and sort
        let results =
            Seq.zip resultData labels
            |> Seq.sortByDescending fst
            |> Seq.take 10

        // print the top 10 results
        for (percent, label) in results do
            printfn "%f %s" percent label

    [<EntryPoint>]
    let main argv = 
        // unpack arguments related to the model
        let modelFile   = argv.[0]
        let trainedFile = argv.[1]
        let meanFile    = argv.[2]
        let labelFile   = argv.[3]

        // unpack the argument for the files to be tested
        let imageFilePath = argv.[4]

        // three color images, so three channels in our matrix
        let numChannels = 3 

        // process one image concurrently
        let imagesToProcess = 1

        // load the model and training data
        let net = loadModel modelFile trainedFile 

        // grab a reference to the input blob where will put the image
        let inputBlob = net.InputBlobs |> Seq.head

        // size structure 
        let blobSize = new Size(inputBlob.Shape(Shape.Width), inputBlob.Shape(Shape.Height))

        // reshapre the input blob and network to suite the number of images and channels we have
        inputBlob.Reshape([|imagesToProcess; numChannels; blobSize.Width; blobSize.Height|])
        net.Reshape()

        // load the input image into the input blob
        loadImageIntoBlob imageFilePath blobSize meanFile inputBlob numChannels

        // forward the data though the network
        let loss = ref 0.0f
        net.Forward(loss)

        // print the results
        printOutput net labelFile

        // exit zero
        0