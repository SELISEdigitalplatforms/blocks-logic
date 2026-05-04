import React, { useEffect, useState } from "react";

interface AnnotationBarProps {
  annotationDuration: number;
}

const AnnotationBar = ({ annotationDuration }: AnnotationBarProps) => {
  const [annotation, setAnnotation] = useState<number[]>([]);

  useEffect(() => {
    if (annotationDuration) {
      const duration = annotationDuration;

      const annotationArr = [];
      for (let stop = 0; stop <= duration; stop += duration / 5) {
        annotationArr.push(parseFloat(stop.toFixed(3)));
      }
      setAnnotation(annotationArr);
    }
  }, [annotationDuration]);

  return (
    <div className="flex w-full flex-col items-center overflow-y-auto bg-slate-100 dark:bg-slate-900">
      <div className="mb-1 flex w-full justify-between border-b">
        {annotation.slice(0, annotation.length - 1).map((annotationTime, traceIndex: number) => (
          <div
            key={traceIndex}
            className={`flex w-full justify-between border-x px-[2px] py-[4px]`}
          >
            <span className="text-[12px] font-medium text-low-emphasis">{annotationTime}ms</span>
            {traceIndex === annotation.length - 2 && (
              <span className="ml-[12px] text-[12px] font-medium text-low-emphasis">
                {annotation[annotation.length - 1]}ms
              </span>
            )}
          </div>
        ))}
      </div>
      <div className="my-2 h-3 w-full bg-chart-purple" />
    </div>
  );
};

export default AnnotationBar;
