namespace UnitTests.FSharpGrains

open System.Threading.Tasks
open UnitTests.GrainInterfaces
open Forkleans
open Forkleans.Metadata

[<GrainType("fsharp.generic`1")>]
type Generic1ArgumentGrain<'T>() = 
    inherit Grain()

    interface INonGenericBase with 
        member x.Ping() = Task.CompletedTask

    interface IGeneric1Argument<'T> with 
        member x.Ping(t:'T) = Task.FromResult(t);
