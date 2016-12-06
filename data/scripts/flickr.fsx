(*
0. Warmup: downloading a picture from the web
++++++++++++++++++++++++++++++++++++++++++++++++++++++
*)

open System.Net
open System.Drawing

let downloadImage (targetPath:string) (url:string) =
    let request = url |> WebRequest.Create
    use response = request.GetResponse ()
    use stream = response.GetResponseStream ()
    let image = stream |> Image.FromStream
    image.Save (targetPath)

let logoUrl = """http://fsharp.org/img/logo.png"""
let saveFolder = __SOURCE_DIRECTORY__ + "/logodemo.png"

logoUrl |> downloadImage saveFolder

(*
1. Finding random pictures from Flickr
++++++++++++++++++++++++++++++++++++++++++++++++++++++ 
*)

#r @"..\..\packages\FSharp.Data\lib\net40\FSharp.Data.dll"
open FSharp.Data
#r @"System.Xml.Linq.dll"
open System.Xml.Linq

let apiKey = "99bc6e017593e1d5f41f6e0d09cb7286"

// sample: search for cats pictures...
type SearchResults = XmlProvider<"""https://api.flickr.com/services/rest/?method=flickr.photos.search&api_key=99bc6e017593e1d5f41f6e0d09cb7286&text=cats&format=rest""">

let flickrSearch (searchTerm:string) =
    sprintf """https://api.flickr.com/services/rest/?method=flickr.photos.search&api_key=99bc6e017593e1d5f41f6e0d09cb7286&text=%s&format=rest""" searchTerm
    |> SearchResults.Load

let flickrPhotoUrl (photo:SearchResults.Photo) = 
    sprintf "https://farm%i.staticflickr.com/%i/%i_%s.jpg" photo.Farm photo.Server photo.Id photo.Secret.Value   
    
    
(*
2. Creating a sample
++++++++++++++++++++++++++++++++++++++++++++++++++++++ 
*)

let terms = [ 
    "cat"
    "dog"
    "mouse"
    "fish"
    "hammer"
    "knife"
    "spatula"
    "envelope" ]

let sampleImages =
    terms
    |> List.map flickrSearch
    |> List.map (fun results ->
        let photos = results.Photos.Photos 
        photos 
        |> Seq.groupBy (fun p -> p.Owner)
        |> Seq.map (fun (owner, pics) -> pics |> Seq.head)
        |> Seq.truncate 5
        |> Seq.map flickrPhotoUrl)
    |> Seq.concat
    |> Seq.toList

// Download the sample

let sampleSource = __SOURCE_DIRECTORY__ + """\..\"""
let sampleFolder = System.IO.Directory.CreateDirectory sampleSource

sampleImages 
|> Seq.iteri (fun i url -> downloadImage (sampleSource + sprintf "image%i.jpg" i) url)

