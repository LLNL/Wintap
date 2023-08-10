using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.etl.load.interfaces
{
    public interface IUpload
    {
        string Name { get; set; }

        /// <summary>
        /// Entry point for adapters to perform upload setup tasks, if any.
        /// Gets called once per upload cycle 
        /// </summary>
        /// <returns></returns>
        bool PreUpload(Dictionary<string,string> parameters);
        /// <summary>
        /// Method that uploads a single file to somewhere 
        /// </summary>
        /// <param name="localFile"></param>
        /// <returns></returns>
        bool Upload(string localFile);
        /// <summary>
        /// Post upload tasks, if any
        /// </summary>
        /// <returns></returns>
        bool PostUpload();
    }
}
