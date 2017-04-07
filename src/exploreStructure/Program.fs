namespace Caffe.Clr.Examples
module Classification =

    open System.IO
    open System.Drawing
    open System.Drawing.Imaging
    open System.Runtime.InteropServices
    open Caffe.Clr

    let loadModel modelFile  =
        // load the network and training data
        let net = new Net(modelFile, Phase.Test)

        // check the network has the right number of inputs/outputs
        assert (net.InputBlobs.Count = 1)
        assert (net.OutputBlobs.Count = 1)

        net 


    [<EntryPoint>]
    let main argv = 
        // unpack arguments related to the model
        let modelFile   = argv.[0]
//        let trainedFile = argv.[1]
//        let meanFile    = argv.[2]
//        let labelFile   = argv.[3]

        // unpack the argument for the files to be tested
        //let imageFilePath = argv.[4]

        // load the model and training data
        let net = loadModel modelFile 

        for name in net.LayerNames do
            let layer = net.LayerByName name
            match layer with
            | Some layer -> 
                printfn "%s\t\t%s\t\t%i" name layer.Type layer.Blobs.Count
                for blob in layer.Blobs do
                    printfn "\t\t%i" blob.Count
            | None -> ()

//        for name in net.BlobNames do
//            let blob = net.BlobByName name
//            match blob with
//            | Some blob -> printfn "%s %i" name blob.Count
//            | None -> ()

        // exit zero
        0