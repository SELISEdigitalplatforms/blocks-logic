import { Link } from "react-router-dom";
import { Button } from "@/components/ui-kits/button/button";
import { motion } from "framer-motion";
import { Layers, MoveRight } from "lucide-react";

export default function ConsoleCreateProject() {
  return (
    <motion.div
      initial={{ opacity: 0, y: 14 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.5, ease: [0.22, 1, 0.36, 1] }}
      className="relative flex flex-col items-center gap-7 overflow-hidden rounded-2xl border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] py-28 text-center shadow-sm"
    >
      {/* Top edge glow bar */}
      <div className="pointer-events-none absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-primary/70 to-transparent" />

      {/* Primary radial gradient — deep, multi-stop */}
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_85%_55%_at_50%_-10%,hsl(var(--primary)/0.20),hsl(var(--primary)/0.06)_55%,transparent_80%)]" />

      {/* Aurora orbs for depth */}
      <div className="pointer-events-none absolute -top-10 left-1/3 h-72 w-72 -translate-x-1/2 rounded-full bg-primary/10 blur-3xl" />
      <div className="pointer-events-none absolute -top-10 right-1/3 h-72 w-72 translate-x-1/2 rounded-full bg-violet-500/8 blur-3xl" />

      {/* Dot grid — masked to fade toward edges */}
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(hsl(var(--border-default))_1px,transparent_1px)] [background-size:22px_22px] [mask-image:radial-gradient(ellipse_75%_75%_at_50%_40%,black_30%,transparent_100%)] opacity-50" />

      {/* Icon */}
      <div className="relative flex h-16 w-16 items-center justify-center rounded-2xl border border-primary/20 bg-gradient-to-b from-[hsl(var(--card))] to-[hsl(var(--card)/0.85)] ">
        <div className="pointer-events-none absolute inset-x-0 top-0 h-px rounded-t-2xl " />
        <Layers className="h-8 w-8 text-primary" />
      </div>

      <div className="relative flex flex-col gap-2">
        <h3 className="text-3xl font-semibold tracking-tight text-[hsl(var(--high-emphasis))]">
          Welcome to SELISE Blocks
        </h3>
        <p className="max-w-md text-sm leading-relaxed text-muted-foreground">
          Explore and manage all your projects in one place. With SELISE Blocks, building and
          scaling applications has never been easier. Start by creating a project.
        </p>
      </div>

      <div className="relative flex items-center gap-3">
        <Link to="/create-project">
          <Button className="group gap-2 shadow-md shadow-primary/20">
            Create a project
            <MoveRight className="h-4 w-4 transition-transform group-hover:translate-x-1" />
          </Button>
        </Link>
        <Button variant="ghost" disabled>
          View documentation
        </Button>
      </div>
    </motion.div>
  );
}
