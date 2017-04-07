#r "../../packages/caffe.clr/lib/net452/caffe.clr.dll"
open System
open System.IO
open Caffe.Clr

let newPath = Environment.GetEnvironmentVariable("PATH") + ";" + Path.GetFullPath(Path.Combine( __SOURCE_DIRECTORY__,  "../../bin/deepdream"))
Environment.SetEnvironmentVariable("PATH", newPath)
//Environment.Is64BitProcess

let modelFile = __SOURCE_DIRECTORY__ + "/../../bvlc_googlenet/deploy.prototxt"
let net = new Net(modelFile, Phase.Test)
