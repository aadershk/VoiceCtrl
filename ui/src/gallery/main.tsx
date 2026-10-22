import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "../design/design.css";
import "./gallery.css";
import { markFirstPaintDone } from "../design/motion";
import { Gallery } from "./Gallery";

const root = document.getElementById("root");
if (!root) throw new Error("gallery.html has no #root");

createRoot(root).render(
  <StrictMode>
    <Gallery />
  </StrictMode>,
);
markFirstPaintDone();
